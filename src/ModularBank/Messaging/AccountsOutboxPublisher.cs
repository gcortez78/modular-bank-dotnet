using System.Text;
using FinBank.IntegrationEvents;
using FinBank.RabbitMqResilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularBank.Modules.Accounts.Infrastructure;
using RabbitMQ.Client;

namespace ModularBank.Messaging;

public sealed class AccountsOutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqSagaOptions> options,
    ILogger<AccountsOutboxPublisher> logger) : BackgroundService
{
    private readonly RabbitMqSagaOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error no controlado en Accounts Outbox.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(
                    Math.Max(1, _options.OutboxPollingIntervalSeconds)),
                stoppingToken);
        }
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        var pending = await db.AccountOutboxMessages
            .Where(x => x.ProcessedAt == null &&
                        x.Attempts < _options.OutboxMaxAttempts)
            .OrderBy(x => x.OccurredAt)
            .Take(Math.Max(1, _options.OutboxBatchSize))
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return;

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(
            "finbank-monolith-accounts-outbox",
            cancellationToken);

        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await channel.ExchangeDeclareAsync(
            _options.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        foreach (var message in pending)
        {
            try
            {
                var metadata = CloudEventJson.ReadMetadata(message.PayloadJson);
                var properties = CloudEventBasicProperties.Create(
                    metadata,
                    message.OccurredAt,
                    "monolith-accounts");

                await channel.BasicPublishAsync(
                    _options.Exchange,
                    message.RoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.PayloadJson),
                    cancellationToken: cancellationToken);

                message.MarkProcessed(DateTimeOffset.UtcNow);

                logger.LogInformation(
                    "Accounts publicó CloudEvent {EventType} {EventId}; CorrelationId {CorrelationId}.",
                    metadata.Type,
                    metadata.Id,
                    metadata.CorrelationId);
            }
            catch (Exception ex)
            {
                message.RegisterFailure(ex.Message);

                logger.LogWarning(
                    ex,
                    "No se pudo publicar Accounts Outbox {EventId}. Intento {Attempt}.",
                    message.Id,
                    message.Attempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

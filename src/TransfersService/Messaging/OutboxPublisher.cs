using System.Text;
using FinBank.IntegrationEvents;
using FinBank.RabbitMqResilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TransfersService.Infrastructure;

namespace TransfersService.Messaging;

public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> rabbitOptions,
    IOptions<OutboxOptions> outboxOptions,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly RabbitMqOptions _rabbit = rabbitOptions.Value;
    private readonly OutboxOptions _outbox = outboxOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error no controlado en Transfers Outbox.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, _outbox.PollingIntervalSeconds)),
                stoppingToken);
        }
    }

    private async Task PublishPendingMessagesAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransfersDbContext>();

        var pending = await db.OutboxMessages
            .Where(x => x.ProcessedAt == null && x.Attempts < _outbox.MaxAttempts)
            .OrderBy(x => x.OccurredAt)
            .Take(Math.Max(1, _outbox.BatchSize))
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return;

        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Host,
            Port = _rabbit.Port,
            UserName = _rabbit.UserName,
            Password = _rabbit.Password,
            VirtualHost = _rabbit.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(
            "finbank-transfers-outbox",
            cancellationToken);

        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await channel.ExchangeDeclareAsync(
            _rabbit.Exchange,
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
                    "transfers-service");

                await channel.BasicPublishAsync(
                    _rabbit.Exchange,
                    message.RoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.PayloadJson),
                    cancellationToken: cancellationToken);

                message.MarkProcessed(DateTimeOffset.UtcNow);

                logger.LogInformation(
                    "CloudEvent {EventType} {EventId} publicado con routing key {RoutingKey}; CorrelationId {CorrelationId}.",
                    metadata.Type,
                    metadata.Id,
                    message.RoutingKey,
                    metadata.CorrelationId);
            }
            catch (Exception ex)
            {
                message.RegisterFailure(ex.Message);

                logger.LogWarning(
                    ex,
                    "No se pudo publicar Transfers Outbox {EventId}. Intento {Attempt}.",
                    message.Id,
                    message.Attempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

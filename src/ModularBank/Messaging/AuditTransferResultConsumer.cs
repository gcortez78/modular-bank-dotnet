using System.Globalization;
using FinBank.IntegrationEvents;
using FinBank.IntegrationEvents.Contracts;
using FinBank.RabbitMqResilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularBank.Messaging;
using ModularBank.Modules.Accounts.Infrastructure;
using ModularBank.Modules.Audit.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ModularBank.Messaging;

public sealed class AuditTransferResultConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqSagaOptions> options,
    ILogger<AuditTransferResultConsumer> logger) : BackgroundService
{
    private const string ConsumerName = "audit.transfer-results.v1";
    private readonly RabbitMqSagaOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "El consumidor Audit se desconectó; reconexión en 5 segundos.");

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
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
            "finbank-monolith-audit-transfer-results",
            cancellationToken);

        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        var topology = BuildTopology();
        await RabbitMqResilienceTopology.DeclareAsync(
            channel,
            topology,
            cancellationToken);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
            await HandleAsync(channel, topology, eventArgs, cancellationToken);

        await channel.BasicConsumeAsync(
            queue: _options.AuditQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Audit consume resultados desde {Queue}; retry {RetryDelays}.",
            _options.AuditQueue,
            _options.RetryDelaysSeconds);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task HandleAsync(
        IChannel channel,
        RabbitMqConsumerTopology topology,
        BasicDeliverEventArgs eventArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            var routingKey =
                RabbitMqFailureHandler.GetEffectiveRoutingKey(eventArgs);

            if (routingKey == _options.TransferCompletedRoutingKey)
            {
                var cloudEvent = CloudEventJson.DeserializeAndValidate<TransferCompletedV1>(
                    eventArgs.Body.Span,
                    EventCatalog.TransferCompletedV1,
                    _options.MaxPayloadBytes);

                await RecordCompletedAsync(cloudEvent, cancellationToken);
            }
            else if (routingKey == _options.TransferFailedRoutingKey)
            {
                var cloudEvent = CloudEventJson.DeserializeAndValidate<TransferFailedV1>(
                    eventArgs.Body.Span,
                    EventCatalog.TransferFailedV1,
                    _options.MaxPayloadBytes);

                await RecordFailedAsync(cloudEvent, cancellationToken);
            }
            else
            {
                throw new NotSupportedException(
                    $"Routing key no soportada: {routingKey}");
            }

            await channel.BasicAckAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var result = await RabbitMqFailureHandler.HandleAsync(
                channel,
                eventArgs,
                topology,
                ex,
                cancellationToken);

            logger.LogWarning(
                ex,
                "Audit no procesó el evento. Disposición {Disposition}; retry {RetryCount}; destino {Destination}; MessageId {MessageId}.",
                result.Disposition,
                result.RetryCount,
                result.Destination,
                eventArgs.BasicProperties.MessageId);
        }
    }

    private async Task RecordCompletedAsync(
        CloudEvent<TransferCompletedV1> cloudEvent,
        CancellationToken cancellationToken)
    {
        var data = cloudEvent.Data;

        await RecordAsync(
            cloudEvent.Id,
            data.UserId,
            "TRANSFER_COMPLETED",
            new Dictionary<string, string>
            {
                ["eventId"] = cloudEvent.Id.ToString(),
                ["correlationId"] = cloudEvent.CorrelationId.ToString(),
                ["causationId"] = cloudEvent.CausationId?.ToString() ?? string.Empty,
                ["transferId"] = data.TransferId.ToString(),
                ["sourceAccountId"] = data.SourceAccountId.ToString(),
                ["targetAccountId"] = data.TargetAccountId.ToString(),
                ["amount"] = data.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                ["currency"] = data.Currency,
                ["reference"] = data.Reference ?? string.Empty
            },
            cancellationToken);
    }

    private async Task RecordFailedAsync(
        CloudEvent<TransferFailedV1> cloudEvent,
        CancellationToken cancellationToken)
    {
        var data = cloudEvent.Data;

        await RecordAsync(
            cloudEvent.Id,
            data.UserId,
            "TRANSFER_FAILED",
            new Dictionary<string, string>
            {
                ["eventId"] = cloudEvent.Id.ToString(),
                ["correlationId"] = cloudEvent.CorrelationId.ToString(),
                ["causationId"] = cloudEvent.CausationId?.ToString() ?? string.Empty,
                ["transferId"] = data.TransferId.ToString(),
                ["sourceAccountId"] = data.SourceAccountId.ToString(),
                ["targetAccountId"] = data.TargetAccountId.ToString(),
                ["amount"] = data.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                ["currency"] = data.Currency,
                ["reference"] = data.Reference ?? string.Empty,
                ["reason"] = data.Reason
            },
            cancellationToken);
    }

    private async Task RecordAsync(
        Guid eventId,
        Guid userId,
        string action,
        Dictionary<string, string> details,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var alreadyProcessed = await db.SagaInboxMessages.AnyAsync(
            x => x.ConsumerName == ConsumerName && x.EventId == eventId,
            cancellationToken);

        if (alreadyProcessed)
        {
            logger.LogInformation("Audit ya procesó CloudEvent {EventId}.", eventId);
            return;
        }

        await audit.RecordAsync(userId, action, details);

        db.SagaInboxMessages.Add(
            SagaInboxMessage.Create(
                ConsumerName,
                eventId,
                DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Audit registró {Action} para CloudEvent {EventId}.",
            action,
            eventId);
    }

    private RabbitMqConsumerTopology BuildTopology() =>
        new(
            _options.Exchange,
            _options.RetryExchange,
            _options.DeadLetterExchange,
            _options.AuditQueue,
            [_options.TransferCompletedRoutingKey, _options.TransferFailedRoutingKey],
            _options.GetRetryDelays());
}

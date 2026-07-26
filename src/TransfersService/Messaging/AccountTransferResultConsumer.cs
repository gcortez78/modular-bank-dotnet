using FinBank.IntegrationEvents;
using FinBank.IntegrationEvents.Contracts;
using FinBank.RabbitMqResilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TransfersService.Domain;
using TransfersService.Infrastructure;

namespace TransfersService.Messaging;

public sealed class AccountTransferResultConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<AccountTransferResultConsumer> logger) : BackgroundService
{
    private const string ConsumerName = "transfers.account-results.v1";
    private readonly RabbitMqOptions _options = options.Value;

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
                    "El consumidor de resultados de Accounts se desconectó; reconexión en 5 segundos.");

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
            "finbank-transfers-account-result-consumer",
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
            queue: _options.AccountResultQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Transfers consume resultados de Accounts desde {Queue}; retry {RetryDelays}.",
            _options.AccountResultQueue,
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

            if (routingKey == _options.AccountAppliedRoutingKey)
            {
                var cloudEvent = CloudEventJson.DeserializeAndValidate<AccountTransferAppliedV1>(
                    eventArgs.Body.Span,
                    EventCatalog.AccountTransferAppliedV1,
                    _options.MaxPayloadBytes);

                await HandleAppliedAsync(cloudEvent, cancellationToken);
            }
            else if (routingKey == _options.AccountRejectedRoutingKey)
            {
                var cloudEvent = CloudEventJson.DeserializeAndValidate<AccountTransferRejectedV1>(
                    eventArgs.Body.Span,
                    EventCatalog.AccountTransferRejectedV1,
                    _options.MaxPayloadBytes);

                await HandleRejectedAsync(cloudEvent, cancellationToken);
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
                "Resultado de Accounts no procesado. Disposición {Disposition}; retry {RetryCount}; destino {Destination}; MessageId {MessageId}.",
                result.Disposition,
                result.RetryCount,
                result.Destination,
                eventArgs.BasicProperties.MessageId);
        }
    }

    private async Task HandleAppliedAsync(
        CloudEvent<AccountTransferAppliedV1> cloudEvent,
        CancellationToken cancellationToken)
    {
        var data = cloudEvent.Data;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransfersDbContext>();

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        if (await WasProcessedAsync(db, cloudEvent.Id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("CloudEvent {EventId} ya fue procesado por Transfers.", cloudEvent.Id);
            return;
        }

        var transfer = await db.Transfers.SingleOrDefaultAsync(
            x => x.Id == data.TransferId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"No existe la transferencia {data.TransferId}.");

        var occurredAt = DateTimeOffset.UtcNow;

        if (transfer.Status == TransferStatus.Pending)
        {
            transfer.Complete(occurredAt);

            var completed = CloudEventFactory.Create(
                EventCatalog.TransferCompletedV1,
                source: "finbank/transfers-service",
                subject: $"transfer/{transfer.Id:N}",
                correlationId: cloudEvent.CorrelationId,
                causationId: cloudEvent.Id,
                data: new TransferCompletedV1(
                    transfer.Id,
                    transfer.UserId,
                    transfer.SourceAccountId,
                    transfer.TargetAccountId,
                    transfer.Amount,
                    data.Currency,
                    transfer.Reference,
                    occurredAt),
                occurredAt: occurredAt);

            db.OutboxMessages.Add(
                OutboxMessage.Create(
                    completed.Id,
                    completed.Type,
                    _options.TransferCompletedRoutingKey,
                    CloudEventJson.Serialize(completed),
                    occurredAt));
        }

        db.ProcessedIntegrationEvents.Add(
            ProcessedIntegrationEvent.Create(
                cloudEvent.Id,
                ConsumerName,
                occurredAt));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Transferencia {TransferId} confirmada. EventId {EventId}; CorrelationId {CorrelationId}.",
            data.TransferId,
            cloudEvent.Id,
            cloudEvent.CorrelationId);
    }

    private async Task HandleRejectedAsync(
        CloudEvent<AccountTransferRejectedV1> cloudEvent,
        CancellationToken cancellationToken)
    {
        var data = cloudEvent.Data;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransfersDbContext>();

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        if (await WasProcessedAsync(db, cloudEvent.Id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("CloudEvent {EventId} ya fue procesado por Transfers.", cloudEvent.Id);
            return;
        }

        var transfer = await db.Transfers.SingleOrDefaultAsync(
            x => x.Id == data.TransferId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"No existe la transferencia {data.TransferId}.");

        var occurredAt = DateTimeOffset.UtcNow;

        if (transfer.Status == TransferStatus.Pending)
        {
            transfer.Fail(data.Reason, occurredAt);

            var failed = CloudEventFactory.Create(
                EventCatalog.TransferFailedV1,
                source: "finbank/transfers-service",
                subject: $"transfer/{transfer.Id:N}",
                correlationId: cloudEvent.CorrelationId,
                causationId: cloudEvent.Id,
                data: new TransferFailedV1(
                    transfer.Id,
                    transfer.UserId,
                    transfer.SourceAccountId,
                    transfer.TargetAccountId,
                    transfer.Amount,
                    data.Currency,
                    transfer.Reference,
                    data.Reason,
                    occurredAt),
                occurredAt: occurredAt);

            db.OutboxMessages.Add(
                OutboxMessage.Create(
                    failed.Id,
                    failed.Type,
                    _options.TransferFailedRoutingKey,
                    CloudEventJson.Serialize(failed),
                    occurredAt));
        }

        db.ProcessedIntegrationEvents.Add(
            ProcessedIntegrationEvent.Create(
                cloudEvent.Id,
                ConsumerName,
                occurredAt));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Transferencia {TransferId} rechazada por Accounts: {Reason}. EventId {EventId}.",
            data.TransferId,
            data.Reason,
            cloudEvent.Id);
    }

    private static async Task<bool> WasProcessedAsync(
        TransfersDbContext db,
        Guid eventId,
        CancellationToken cancellationToken) =>
        await db.ProcessedIntegrationEvents.AnyAsync(
            x => x.ConsumerName == ConsumerName && x.EventId == eventId,
            cancellationToken);

    private RabbitMqConsumerTopology BuildTopology() =>
        new(
            _options.Exchange,
            _options.RetryExchange,
            _options.DeadLetterExchange,
            _options.AccountResultQueue,
            [_options.AccountAppliedRoutingKey, _options.AccountRejectedRoutingKey],
            _options.GetRetryDelays());
}

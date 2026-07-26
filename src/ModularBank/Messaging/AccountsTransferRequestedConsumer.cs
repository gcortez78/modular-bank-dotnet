using System.Data;
using FinBank.IntegrationEvents;
using FinBank.IntegrationEvents.Contracts;
using FinBank.RabbitMqResilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularBank.Modules.Accounts.Domain;
using ModularBank.Modules.Accounts.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ModularBank.Messaging;

public sealed class AccountsTransferRequestedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqSagaOptions> options,
    ILogger<AccountsTransferRequestedConsumer> logger) : BackgroundService
{
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
                    "El consumidor Accounts/TransferRequested se desconectó; reconexión en 5 segundos.");

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var factory = CreateFactory();

        await using var connection = await factory.CreateConnectionAsync(
            "finbank-monolith-accounts-transfer-requested",
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
            prefetchCount: 1,
            global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
            await HandleAsync(channel, topology, eventArgs, cancellationToken);

        await channel.BasicConsumeAsync(
            queue: _options.AccountsQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Accounts consume {RoutingKey} desde {Queue}; retry {RetryDelays}.",
            _options.TransferRequestedRoutingKey,
            _options.AccountsQueue,
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

            if (routingKey != _options.TransferRequestedRoutingKey)
            {
                throw new NotSupportedException(
                    $"Routing key no soportada: {routingKey}");
            }

            var cloudEvent = CloudEventJson.DeserializeAndValidate<TransferRequestedV1>(
                eventArgs.Body.Span,
                EventCatalog.TransferRequestedV1,
                _options.MaxPayloadBytes);

            await ProcessAsync(cloudEvent, cancellationToken);

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
                "TransferRequested no procesado. Disposición {Disposition}; retry {RetryCount}; destino {Destination}; MessageId {MessageId}.",
                result.Disposition,
                result.RetryCount,
                result.Destination,
                eventArgs.BasicProperties.MessageId);
        }
    }

    private async Task ProcessAsync(
        CloudEvent<TransferRequestedV1> cloudEvent,
        CancellationToken cancellationToken)
    {
        var data = cloudEvent.Data;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var alreadyProcessed = await db.ProcessedTransferCommands.AnyAsync(
            x => x.TransferId == data.TransferId ||
                 x.RequestEventId == cloudEvent.Id,
            cancellationToken);

        if (alreadyProcessed)
        {
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "CloudEvent TransferRequested {EventId} ya fue procesado. CorrelationId {CorrelationId}.",
                cloudEvent.Id,
                cloudEvent.CorrelationId);

            return;
        }

        var processedAt = DateTimeOffset.UtcNow;
        var result = "Applied";
        string? failureReason = null;

        var source = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == data.SourceAccountId,
            cancellationToken);

        var target = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == data.TargetAccountId,
            cancellationToken);

        if (source is null)
        {
            result = "Rejected";
            failureReason = "Cuenta origen no encontrada.";
        }
        else if (source.UserId != data.UserId)
        {
            result = "Rejected";
            failureReason = "La cuenta origen no pertenece al usuario.";
        }
        else if (target is null)
        {
            result = "Rejected";
            failureReason = "Cuenta destino no encontrada.";
        }
        else if (source.Balance < data.Amount)
        {
            result = "Rejected";
            failureReason = "Saldo insuficiente.";
        }
        else
        {
            source.Balance -= data.Amount;
            target.Balance += data.Amount;
        }

        Guid resultEventId;
        string eventType;
        string routingKey;
        string payload;

        if (result == "Applied")
        {
            var applied = CloudEventFactory.Create(
                EventCatalog.AccountTransferAppliedV1,
                source: "finbank/monolith/accounts",
                subject: $"transfer/{data.TransferId:N}",
                correlationId: cloudEvent.CorrelationId,
                causationId: cloudEvent.Id,
                data: new AccountTransferAppliedV1(
                    data.TransferId,
                    data.UserId,
                    data.SourceAccountId,
                    data.TargetAccountId,
                    data.Amount,
                    data.Currency,
                    data.Reference,
                    processedAt),
                occurredAt: processedAt);

            resultEventId = applied.Id;
            eventType = applied.Type;
            routingKey = _options.AccountAppliedRoutingKey;
            payload = CloudEventJson.Serialize(applied);
        }
        else
        {
            var rejected = CloudEventFactory.Create(
                EventCatalog.AccountTransferRejectedV1,
                source: "finbank/monolith/accounts",
                subject: $"transfer/{data.TransferId:N}",
                correlationId: cloudEvent.CorrelationId,
                causationId: cloudEvent.Id,
                data: new AccountTransferRejectedV1(
                    data.TransferId,
                    data.UserId,
                    data.SourceAccountId,
                    data.TargetAccountId,
                    data.Amount,
                    data.Currency,
                    data.Reference,
                    failureReason!,
                    processedAt),
                occurredAt: processedAt);

            resultEventId = rejected.Id;
            eventType = rejected.Type;
            routingKey = _options.AccountRejectedRoutingKey;
            payload = CloudEventJson.Serialize(rejected);
        }

        db.ProcessedTransferCommands.Add(
            ProcessedTransferCommand.Create(
                data.TransferId,
                cloudEvent.Id,
                data.UserId,
                data.SourceAccountId,
                data.TargetAccountId,
                data.Amount,
                data.Reference,
                result,
                failureReason,
                processedAt));

        db.AccountOutboxMessages.Add(
            AccountOutboxMessage.Create(
                resultEventId,
                eventType,
                routingKey,
                payload,
                processedAt));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Accounts procesó transferencia {TransferId}: {Result}. EventId {EventId}; CorrelationId {CorrelationId}.",
            data.TransferId,
            result,
            cloudEvent.Id,
            cloudEvent.CorrelationId);
    }

    private ConnectionFactory CreateFactory() =>
        new()
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

    private RabbitMqConsumerTopology BuildTopology() =>
        new(
            _options.Exchange,
            _options.RetryExchange,
            _options.DeadLetterExchange,
            _options.AccountsQueue,
            [_options.TransferRequestedRoutingKey],
            _options.GetRetryDelays());
}

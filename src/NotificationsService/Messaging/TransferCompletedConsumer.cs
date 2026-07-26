using System.Globalization;
using FinBank.IntegrationEvents;
using FinBank.IntegrationEvents.Contracts;
using FinBank.NotificationsService.Application;
using FinBank.NotificationsService.Application.Contracts;
using FinBank.NotificationsService.Domain;
using FinBank.RabbitMqResilience;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinBank.NotificationsService.Messaging;

public sealed class TransferCompletedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<TransferCompletedConsumer> logger) : BackgroundService
{
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
                    "El consumidor de TransferCompleted se desconectó; reconexión en 5 segundos.");

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
            "finbank-notifications-transfer-consumer",
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
            queue: _options.Queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Notifications consume {RoutingKey} desde {Queue}; retry {RetryDelays}.",
            _options.RoutingKey,
            _options.Queue,
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

            if (routingKey != _options.RoutingKey)
            {
                throw new NotSupportedException(
                    $"Routing key no soportada: {routingKey}");
            }

            var cloudEvent = CloudEventJson.DeserializeAndValidate<TransferCompletedV1>(
                eventArgs.Body.Span,
                EventCatalog.TransferCompletedV1,
                _options.MaxPayloadBytes);

            var data = cloudEvent.Data;

            await using var scope = scopeFactory.CreateAsyncScope();
            var notifications =
                scope.ServiceProvider.GetRequiredService<INotificationsService>();

            var payload = new Dictionary<string, string>
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
            };

            await notifications.CreateAsync(
                new CreateNotificationRequest(
                    UserId: data.UserId,
                    Type: NotificationType.TransferSent,
                    Payload: payload,
                    IdempotencyKey: $"transfer-completed:{cloudEvent.Id:N}"),
                cancellationToken);

            await channel.BasicAckAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                cancellationToken);

            logger.LogInformation(
                "CloudEvent {EventId} consumido; notificación creada para transferencia {TransferId}; CorrelationId {CorrelationId}.",
                cloudEvent.Id,
                data.TransferId,
                cloudEvent.CorrelationId);
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
                "TransferCompleted no procesado. Disposición {Disposition}; retry {RetryCount}; destino {Destination}; MessageId {MessageId}.",
                result.Disposition,
                result.RetryCount,
                result.Destination,
                eventArgs.BasicProperties.MessageId);
        }
    }

    private RabbitMqConsumerTopology BuildTopology() =>
        new(
            _options.Exchange,
            _options.RetryExchange,
            _options.DeadLetterExchange,
            _options.Queue,
            [_options.RoutingKey],
            _options.GetRetryDelays());
}

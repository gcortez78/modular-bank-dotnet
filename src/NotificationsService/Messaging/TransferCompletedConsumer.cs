using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using FinBank.NotificationsService.Application;
using FinBank.NotificationsService.Application.Contracts;
using FinBank.NotificationsService.Domain;
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
                    "El consumidor de TransferCompleted se desconectó; se reintentará.");

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
            TopologyRecoveryEnabled = true,
            };

        await using var connection =
            await factory.CreateConnectionAsync(
                "finbank-notifications-transfer-consumer",
                cancellationToken);

        await using var channel =
            await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await DeclareTopologyAsync(channel, cancellationToken);
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            await HandleAsync(channel, eventArgs, cancellationToken);
        };

        await channel.BasicConsumeAsync(
            queue: _options.Queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Consumidor conectado a {Queue} con routing key {RoutingKey}.",
            _options.Queue,
            _options.RoutingKey);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task DeclareTopologyAsync(
        IChannel channel,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            _options.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            _options.DeadLetterExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var deadLetterRoutingKey = $"{_options.Queue}.dead";
        var deadLetterQueue = $"{_options.Queue}.dlq";

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = deadLetterRoutingKey
        };

        await channel.QueueDeclareAsync(
            queue: _options.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: _options.Queue,
            exchange: _options.Exchange,
            routingKey: _options.RoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: deadLetterQueue,
            exchange: _options.DeadLetterExchange,
            routingKey: deadLetterRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);
    }

    private async Task HandleAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            var integrationEvent =
                JsonSerializer.Deserialize<TransferCompletedIntegrationEvent>(
                    eventArgs.Body.Span,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new JsonException("El evento no contiene datos.");

            if (integrationEvent.EventId == Guid.Empty ||
                integrationEvent.TransferId == Guid.Empty ||
                integrationEvent.UserId == Guid.Empty)
            {
                throw new JsonException(
                    "El evento no contiene identificadores válidos.");
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var notifications =
                scope.ServiceProvider.GetRequiredService<INotificationsService>();

            var payload = new Dictionary<string, string>
            {
                ["eventId"] = integrationEvent.EventId.ToString(),
                ["transferId"] = integrationEvent.TransferId.ToString(),
                ["sourceAccountId"] = integrationEvent.SourceAccountId.ToString(),
                ["targetAccountId"] = integrationEvent.TargetAccountId.ToString(),
                ["amount"] = integrationEvent.Amount.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture),
                ["currency"] = integrationEvent.Currency,
                ["reference"] = integrationEvent.Reference ?? string.Empty
            };

            await notifications.CreateAsync(
                new CreateNotificationRequest(
                    UserId: integrationEvent.UserId,
                    Type: NotificationType.TransferSent,
                    Payload: payload,
                    IdempotencyKey:
                        $"transfer-completed:{integrationEvent.EventId:N}"),
                cancellationToken);

            await channel.BasicAckAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                cancellationToken);

            logger.LogInformation(
                "Evento {EventId} consumido; notificación creada para transferencia {TransferId}.",
                integrationEvent.EventId,
                integrationEvent.TransferId);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Evento inválido enviado a la DLQ. DeliveryTag {DeliveryTag}.",
                eventArgs.DeliveryTag);

            await channel.BasicRejectAsync(
                eventArgs.DeliveryTag,
                requeue: false,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Falló el procesamiento del evento; se reencolará. DeliveryTag {DeliveryTag}.",
                eventArgs.DeliveryTag);

            await channel.BasicNackAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
        }
    }
}

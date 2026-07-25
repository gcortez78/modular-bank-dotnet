using RabbitMQ.Client;

namespace FinBank.RabbitMqResilience;

public static class RabbitMqResilienceTopology
{
    public static async Task DeclareAsync(
        IChannel channel,
        RabbitMqConsumerTopology topology,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            topology.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            topology.RetryExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            topology.DeadLetterExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var mainQueueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = topology.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = topology.DeadLetterRoutingKey
        };

        await channel.QueueDeclareAsync(
            topology.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: mainQueueArguments,
            cancellationToken: cancellationToken);

        foreach (var bindingKey in topology.BindingKeys)
        {
            await channel.QueueBindAsync(
                topology.Queue,
                topology.Exchange,
                bindingKey,
                arguments: null,
                cancellationToken: cancellationToken);
        }

        await channel.QueueDeclareAsync(
            topology.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            topology.DeadLetterQueue,
            topology.DeadLetterExchange,
            topology.DeadLetterRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        for (var index = 0; index < topology.RetryDelays.Count; index++)
        {
            var attempt = index + 1;
            var retryQueueArguments = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = checked((int)topology.RetryDelays[index].TotalMilliseconds),
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = topology.Queue
            };

            await channel.QueueDeclareAsync(
                topology.RetryQueue(attempt),
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: retryQueueArguments,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                topology.RetryQueue(attempt),
                topology.RetryExchange,
                topology.RetryRoutingKey(attempt),
                arguments: null,
                cancellationToken: cancellationToken);
        }
    }
}

using System.Text;
using System.Text.Json;
using FinBank.IntegrationEvents.Validation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinBank.RabbitMqResilience;

public enum MessageFailureDisposition
{
    Retried,
    DeadLettered
}

public sealed record MessageFailureResult(
    MessageFailureDisposition Disposition,
    int RetryCount,
    string Destination);

public static class RabbitMqFailureHandler
{
    public const string RetryHeader = "x-finbank-retry-count";
    public const string OriginalRoutingKeyHeader =
        "x-finbank-original-routing-key";
    public const string LastErrorHeader = "x-finbank-last-error";
    public const string FailedAtHeader = "x-finbank-failed-at";

    public static bool IsPermanent(Exception exception) =>
        exception is EventContractException or
        JsonException or
        ArgumentException or
        NotSupportedException;

    public static string GetEffectiveRoutingKey(
        BasicDeliverEventArgs delivery)
    {
        var originalRoutingKey = ReadHeaderString(
            delivery.BasicProperties.Headers,
            OriginalRoutingKeyHeader);

        return string.IsNullOrWhiteSpace(originalRoutingKey)
            ? delivery.RoutingKey
            : originalRoutingKey;
    }

    public static async Task<MessageFailureResult> HandleAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        RabbitMqConsumerTopology topology,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var currentRetry = ReadRetryCount(
            delivery.BasicProperties.Headers);

        var originalRoutingKey = GetEffectiveRoutingKey(delivery);

        if (
            IsPermanent(exception) ||
            currentRetry >= topology.RetryDelays.Count
        )
        {
            var properties = CopyProperties(
                delivery.BasicProperties,
                currentRetry,
                originalRoutingKey,
                exception);

            await channel.BasicPublishAsync(
                topology.DeadLetterExchange,
                topology.DeadLetterRoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: delivery.Body,
                cancellationToken: cancellationToken);

            await channel.BasicAckAsync(
                delivery.DeliveryTag,
                multiple: false,
                cancellationToken);

            return new MessageFailureResult(
                MessageFailureDisposition.DeadLettered,
                currentRetry,
                topology.DeadLetterQueue);
        }

        var nextRetry = currentRetry + 1;

        var retryProperties = CopyProperties(
            delivery.BasicProperties,
            nextRetry,
            originalRoutingKey,
            exception);

        await channel.BasicPublishAsync(
            topology.RetryExchange,
            topology.RetryRoutingKey(nextRetry),
            mandatory: true,
            basicProperties: retryProperties,
            body: delivery.Body,
            cancellationToken: cancellationToken);

        await channel.BasicAckAsync(
            delivery.DeliveryTag,
            multiple: false,
            cancellationToken);

        return new MessageFailureResult(
            MessageFailureDisposition.Retried,
            nextRetry,
            topology.RetryQueue(nextRetry));
    }

    public static int ReadRetryCount(
        IDictionary<string, object?>? headers)
    {
        if (
            headers is null ||
            !headers.TryGetValue(RetryHeader, out var value)
        )
        {
            return 0;
        }

        return value switch
        {
            byte number => number,
            short number => number,
            int number => number,
            long number => checked((int)number),
            byte[] bytes when int.TryParse(
                Encoding.UTF8.GetString(bytes),
                out var parsed) => parsed,
            ReadOnlyMemory<byte> memory when int.TryParse(
                Encoding.UTF8.GetString(memory.Span),
                out var parsed) => parsed,
            string text when int.TryParse(
                text,
                out var parsed) => parsed,
            _ => 0
        };
    }

    private static string? ReadHeaderString(
        IDictionary<string, object?>? headers,
        string name)
    {
        if (
            headers is null ||
            !headers.TryGetValue(name, out var value) ||
            value is null
        )
        {
            return null;
        }

        return value switch
        {
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory =>
                Encoding.UTF8.GetString(memory.Span),
            _ => value.ToString()
        };
    }

    private static BasicProperties CopyProperties(
        IReadOnlyBasicProperties source,
        int retryCount,
        string originalRoutingKey,
        Exception exception)
    {
        var headers = source.Headers is null
            ? new Dictionary<string, object?>()
            : source.Headers.ToDictionary(
                item => item.Key,
                item => item.Value);

        headers[RetryHeader] = retryCount;
        headers[OriginalRoutingKeyHeader] =
            Encoding.UTF8.GetBytes(originalRoutingKey);
        headers[LastErrorHeader] =
            Encoding.UTF8.GetBytes(
                Truncate(exception.Message, 1000));
        headers[FailedAtHeader] =
            Encoding.UTF8.GetBytes(
                DateTimeOffset.UtcNow.ToString("O"));

        return new BasicProperties
        {
            Persistent = true,
            ContentType = source.ContentType,
            ContentEncoding = source.ContentEncoding,
            Type = source.Type,
            MessageId = source.MessageId,
            CorrelationId = source.CorrelationId,
            AppId = source.AppId,
            Timestamp = source.Timestamp,
            Headers = headers
        };
    }

    private static string Truncate(
        string value,
        int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..maxLength];
}

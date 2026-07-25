using System.Text;
using FinBank.IntegrationEvents;
using RabbitMQ.Client;

namespace FinBank.RabbitMqResilience;

public static class CloudEventBasicProperties
{
    public static BasicProperties Create(
        CloudEventMetadata metadata,
        DateTimeOffset occurredAt,
        string producer)
    {
        return new BasicProperties
        {
            Persistent = true,
            ContentType = "application/cloudevents+json; charset=utf-8",
            Type = metadata.Type,
            MessageId = metadata.Id.ToString("N"),
            CorrelationId = metadata.CorrelationId.ToString("N"),
            AppId = producer,
            Timestamp = new AmqpTimestamp(occurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["ce-specversion"] = Encoding.UTF8.GetBytes("1.0"),
                ["ce-source"] = Encoding.UTF8.GetBytes(metadata.Source),
                ["ce-type"] = Encoding.UTF8.GetBytes(metadata.Type),
                ["ce-subject"] = Encoding.UTF8.GetBytes(metadata.Subject),
                ["ce-dataschema"] = Encoding.UTF8.GetBytes(metadata.DataSchema),
                ["correlation-id"] = Encoding.UTF8.GetBytes(
                    metadata.CorrelationId.ToString("N")),
                [RabbitMqFailureHandler.RetryHeader] = 0
            }
        };
    }
}

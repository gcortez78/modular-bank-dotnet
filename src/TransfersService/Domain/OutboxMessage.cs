namespace TransfersService.Domain;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = null!;
    public string RoutingKey { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage()
    {
    }

    public static OutboxMessage Create(
        Guid eventId,
        string eventType,
        string routingKey,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        return new OutboxMessage
        {
            Id = eventId,
            EventType = eventType,
            RoutingKey = routingKey,
            PayloadJson = payloadJson,
            OccurredAt = occurredAt
        };
    }

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
        LastError = null;
    }

    public void RegisterFailure(string error)
    {
        Attempts++;
        LastError = error.Length <= 2000 ? error : error[..2000];
    }
}

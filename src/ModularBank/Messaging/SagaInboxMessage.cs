namespace ModularBank.Messaging;

public sealed class SagaInboxMessage
{
    public string ConsumerName { get; private set; } = null!;
    public Guid EventId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    private SagaInboxMessage()
    {
    }

    public static SagaInboxMessage Create(
        string consumerName,
        Guid eventId,
        DateTimeOffset processedAt)
    {
        return new SagaInboxMessage
        {
            ConsumerName = consumerName,
            EventId = eventId,
            ProcessedAt = processedAt
        };
    }
}

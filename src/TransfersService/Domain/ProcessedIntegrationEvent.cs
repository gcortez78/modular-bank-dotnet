namespace TransfersService.Domain;

public sealed class ProcessedIntegrationEvent
{
    public Guid EventId { get; private set; }
    public string ConsumerName { get; private set; } = null!;
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedIntegrationEvent()
    {
    }

    public static ProcessedIntegrationEvent Create(
        Guid eventId,
        string consumerName,
        DateTimeOffset processedAt)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("El EventId es obligatorio.", nameof(eventId));

        if (string.IsNullOrWhiteSpace(consumerName))
            throw new ArgumentException(
                "El nombre del consumidor es obligatorio.",
                nameof(consumerName));

        return new ProcessedIntegrationEvent
        {
            EventId = eventId,
            ConsumerName = consumerName.Trim(),
            ProcessedAt = processedAt
        };
    }
}

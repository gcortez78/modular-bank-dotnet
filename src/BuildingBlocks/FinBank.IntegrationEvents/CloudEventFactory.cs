using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents;

public static class CloudEventFactory
{
    public static CloudEvent<TData> Create<TData>(
        EventDefinition definition,
        string source,
        string subject,
        Guid correlationId,
        Guid? causationId,
        TData data,
        Guid? eventId = null,
        DateTimeOffset? occurredAt = null)
        where TData : IIntegrationEventData
    {
        var cloudEvent = new CloudEvent<TData>
        {
            Id = eventId ?? Guid.NewGuid(),
            Source = source,
            Type = definition.Type,
            Subject = subject,
            Time = occurredAt ?? DateTimeOffset.UtcNow,
            DataSchema = definition.SchemaUrn,
            CorrelationId = correlationId,
            CausationId = causationId,
            Data = data
        };

        EventContractValidator.Validate(cloudEvent, definition);
        return cloudEvent;
    }
}

namespace FinBank.IntegrationEvents.Validation;

public static class EventContractValidator
{
    public static void Validate<TData>(
        CloudEvent<TData> cloudEvent,
        EventDefinition definition)
        where TData : IIntegrationEventData
    {
        if (!string.Equals(cloudEvent.SpecVersion, "1.0", StringComparison.Ordinal))
            throw new EventContractException("specversion debe ser 1.0.");

        if (cloudEvent.Id == Guid.Empty)
            throw new EventContractException("id es obligatorio.");

        if (string.IsNullOrWhiteSpace(cloudEvent.Source))
            throw new EventContractException("source es obligatorio.");

        if (!string.Equals(cloudEvent.Type, definition.Type, StringComparison.Ordinal))
        {
            throw new EventContractException(
                $"type no soportado. Esperado: {definition.Type}; recibido: {cloudEvent.Type}.");
        }

        if (string.IsNullOrWhiteSpace(cloudEvent.Subject))
            throw new EventContractException("subject es obligatorio.");

        if (cloudEvent.Time == default)
            throw new EventContractException("time es obligatorio.");

        if (!string.Equals(
                cloudEvent.DataContentType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new EventContractException(
                "datacontenttype debe ser application/json.");
        }

        if (!string.Equals(
                cloudEvent.DataSchema,
                definition.SchemaUrn,
                StringComparison.Ordinal))
        {
            throw new EventContractException(
                $"dataschema no soportado. Esperado: {definition.SchemaUrn}.");
        }

        if (cloudEvent.CorrelationId == Guid.Empty)
            throw new EventContractException("correlationid es obligatorio.");

        if (cloudEvent.Data is null)
            throw new EventContractException("data es obligatorio.");

        cloudEvent.Data.Validate();
    }
}

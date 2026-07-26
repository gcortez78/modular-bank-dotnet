using System.Text.Json;
using System.Text.Json.Serialization;
using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents;

public static class CloudEventJson
{
    public static readonly JsonSerializerOptions Options = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    public static string Serialize<TData>(CloudEvent<TData> cloudEvent)
        where TData : IIntegrationEventData =>
        JsonSerializer.Serialize(cloudEvent, Options);

    public static CloudEvent<TData> DeserializeAndValidate<TData>(
        ReadOnlySpan<byte> body,
        EventDefinition definition,
        int maxPayloadBytes)
        where TData : IIntegrationEventData
    {
        if (body.Length == 0)
            throw new EventContractException("El evento no contiene payload.");

        if (body.Length > maxPayloadBytes)
        {
            throw new EventContractException(
                $"El payload excede el máximo permitido de {maxPayloadBytes} bytes.");
        }

        CloudEvent<TData>? cloudEvent;

        try
        {
            cloudEvent = JsonSerializer.Deserialize<CloudEvent<TData>>(
                body,
                Options);
        }
        catch (JsonException ex)
        {
            throw new EventContractException(
                "El payload no es JSON CloudEvents válido.",
                ex);
        }

        if (cloudEvent is null)
            throw new EventContractException("El evento no contiene datos.");

        EventContractValidator.Validate(cloudEvent, definition);
        return cloudEvent;
    }

    public static CloudEventMetadata ReadMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new CloudEventMetadata(
            Id: root.GetProperty("id").GetGuid(),
            Type: root.GetProperty("type").GetString()
                ?? throw new EventContractException("Falta type."),
            Source: root.GetProperty("source").GetString()
                ?? throw new EventContractException("Falta source."),
            Subject: root.GetProperty("subject").GetString()
                ?? throw new EventContractException("Falta subject."),
            DataSchema: root.GetProperty("dataschema").GetString()
                ?? throw new EventContractException("Falta dataschema."),
            CorrelationId: root.GetProperty("correlationid").GetGuid());
    }
}

public sealed record CloudEventMetadata(
    Guid Id,
    string Type,
    string Source,
    string Subject,
    string DataSchema,
    Guid CorrelationId);

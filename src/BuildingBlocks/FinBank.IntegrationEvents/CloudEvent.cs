using System.Text.Json.Serialization;
using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents;

public sealed record CloudEvent<TData>
    where TData : IIntegrationEventData
{
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; init; } = "1.0";

    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; init; } = null!;

    [JsonPropertyName("subject")]
    public string Subject { get; init; } = null!;

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; }

    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; init; } = "application/json";

    [JsonPropertyName("dataschema")]
    public string DataSchema { get; init; } = null!;

    [JsonPropertyName("correlationid")]
    public Guid CorrelationId { get; init; }

    [JsonPropertyName("causationid")]
    public Guid? CausationId { get; init; }

    [JsonPropertyName("data")]
    public TData Data { get; init; } = default!;
}

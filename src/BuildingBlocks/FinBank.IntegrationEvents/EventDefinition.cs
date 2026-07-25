namespace FinBank.IntegrationEvents;

public sealed record EventDefinition(
    string Type,
    string RoutingKey,
    string SchemaUrn);

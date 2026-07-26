namespace ModularBank.Messaging.Contracts;

public sealed record TransferRequestedIntegrationEvent(
    Guid EventId,
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    DateTimeOffset OccurredAtUtc);

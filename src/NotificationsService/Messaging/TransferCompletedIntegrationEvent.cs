namespace FinBank.NotificationsService.Messaging;

public sealed record TransferCompletedIntegrationEvent(
    Guid EventId,
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    DateTimeOffset OccurredAtUtc);

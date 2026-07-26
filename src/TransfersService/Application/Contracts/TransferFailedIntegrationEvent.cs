namespace TransfersService.Application.Contracts;

public sealed record TransferFailedIntegrationEvent(
    Guid EventId,
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    string Reason,
    DateTimeOffset OccurredAtUtc);

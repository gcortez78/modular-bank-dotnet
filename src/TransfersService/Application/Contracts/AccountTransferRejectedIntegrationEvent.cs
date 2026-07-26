namespace TransfersService.Application.Contracts;

public sealed record AccountTransferRejectedIntegrationEvent(
    Guid EventId,
    Guid CausationEventId,
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    string Reason,
    DateTimeOffset RejectedAtUtc);

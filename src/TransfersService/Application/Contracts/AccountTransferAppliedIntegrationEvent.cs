namespace TransfersService.Application.Contracts;

public sealed record AccountTransferAppliedIntegrationEvent(
    Guid EventId,
    Guid CausationEventId,
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    DateTimeOffset AppliedAtUtc);

namespace TransfersService.Infrastructure;

public sealed record AccountsTransferCommand(
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string? Reference);

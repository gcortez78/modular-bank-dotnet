namespace TransfersService.Application.Contracts;

public sealed record CreateTransferRequest(
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string? Reference);

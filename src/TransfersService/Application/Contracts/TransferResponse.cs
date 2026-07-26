using TransfersService.Domain;

namespace TransfersService.Application.Contracts;

public sealed record TransferResponse(
    Guid Id,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string? Reference,
    TransferStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

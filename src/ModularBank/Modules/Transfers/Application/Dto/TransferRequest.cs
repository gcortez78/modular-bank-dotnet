using System.ComponentModel.DataAnnotations;

namespace ModularBank.Modules.Transfers.Application.Dto;

public record TransferRequest(
    [Required] Guid SourceAccountId,
    [Required] Guid TargetAccountId,
    [Range(0.01, double.MaxValue)] decimal Amount,
    string? Reference = null
);

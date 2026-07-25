namespace ModularBank.Modules.Accounts.Domain;

public sealed class ProcessedTransferCommand
{
    public Guid TransferId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid TargetAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Reference { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedTransferCommand()
    {
    }

    public static ProcessedTransferCommand Create(
        Guid transferId,
        Guid userId,
        Guid sourceAccountId,
        Guid targetAccountId,
        decimal amount,
        string? reference)
    {
        return new ProcessedTransferCommand
        {
            TransferId = transferId,
            UserId = userId,
            SourceAccountId = sourceAccountId,
            TargetAccountId = targetAccountId,
            Amount = amount,
            Reference = reference,
            ProcessedAt = DateTimeOffset.UtcNow
        };
    }
}

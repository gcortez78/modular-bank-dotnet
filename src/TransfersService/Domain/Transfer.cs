namespace TransfersService.Domain;

public sealed class Transfer
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid TargetAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Reference { get; private set; }
    public TransferStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    private Transfer()
    {
    }

    public static Transfer Create(
        Guid userId,
        Guid sourceAccountId,
        Guid targetAccountId,
        decimal amount,
        string? reference)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("El usuario es obligatorio.", nameof(userId));

        if (sourceAccountId == Guid.Empty || targetAccountId == Guid.Empty)
            throw new ArgumentException("Las cuentas origen y destino son obligatorias.");

        if (sourceAccountId == targetAccountId)
            throw new ArgumentException("Las cuentas origen y destino deben ser diferentes.");

        if (amount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "El monto debe ser mayor que cero.");

        return new Transfer
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceAccountId = sourceAccountId,
            TargetAccountId = targetAccountId,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            Reference = string.IsNullOrWhiteSpace(reference)
                ? null
                : reference.Trim(),
            Status = TransferStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status != TransferStatus.Pending)
            return;

        Status = TransferStatus.Completed;
        CompletedAt = completedAt;
        FailureReason = null;
    }

    public void Fail(string reason, DateTimeOffset failedAt)
    {
        if (Status != TransferStatus.Pending)
            return;

        Status = TransferStatus.Failed;
        CompletedAt = failedAt;

        var normalized = string.IsNullOrWhiteSpace(reason)
            ? "La transferencia fue rechazada."
            : reason.Trim();

        FailureReason = normalized.Length <= 500
            ? normalized
            : normalized[..500];
    }
}

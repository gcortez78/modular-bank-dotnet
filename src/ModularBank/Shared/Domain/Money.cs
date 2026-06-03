namespace ModularBank.Shared.Domain;

public record Money
{
    public decimal Amount { get; }

    public Money(decimal amount)
    {
        if (amount < 0) throw new ArgumentException("Amount must be non-negative");
        Amount = amount;
    }

    public static Money Of(decimal amount) => new(amount);

    public Money Add(Money other) => new(Amount + other.Amount);
    public Money Subtract(Money other)
    {
        if (other.Amount > Amount)
            throw new InvalidOperationException($"Insufficient funds: cannot subtract {other.Amount} from {Amount}");
        return new(Amount - other.Amount);
    }
    public bool IsGreaterThanOrEqualTo(Money other) => Amount >= other.Amount;
}

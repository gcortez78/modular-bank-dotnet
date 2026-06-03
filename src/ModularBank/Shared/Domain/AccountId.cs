namespace ModularBank.Shared.Domain;

public record AccountId(Guid Value)
{
    public static AccountId Of(Guid value) => new(value);
    public static AccountId Random() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

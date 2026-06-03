namespace ModularBank.Shared.Domain;

public record UserId(Guid Value)
{
    public static UserId Of(Guid value) => new(value);
    public static UserId Random() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

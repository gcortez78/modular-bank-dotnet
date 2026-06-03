namespace ModularBank.Modules.Transfers.Domain;

public class Transfer
{
    public Guid Id { get; set; }
    public Guid SourceAccountId { get; set; }
    public Guid TargetAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
}

namespace TransfersService.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollingIntervalSeconds { get; init; } = 2;
    public int BatchSize { get; init; } = 20;
    public int MaxAttempts { get; init; } = 20;
}

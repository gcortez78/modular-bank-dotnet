namespace TransfersService.Infrastructure;

public sealed class MonolithOptions
{
    public const string SectionName = "Monolith";

    public string BaseUrl { get; init; } = null!;
    public string InternalApiKey { get; init; } = null!;
}

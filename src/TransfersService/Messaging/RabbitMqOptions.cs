using FinBank.RabbitMqResilience;

namespace TransfersService.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = "rabbitmq";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "finbank";
    public string Password { get; init; } = null!;
    public string VirtualHost { get; init; } = "/";

    public string Exchange { get; init; } = "finbank.events";
    public string RetryExchange { get; init; } = "finbank.events.retry";
    public string DeadLetterExchange { get; init; } = "finbank.events.dlx";
    public string RetryDelaysSeconds { get; init; } = "5,20,80";
    public int MaxPayloadBytes { get; init; } = 262_144;

    public string TransferRequestedRoutingKey { get; init; } =
        "transfers.requested.v1";

    public string AccountResultQueue { get; init; } =
        "transfers.account-results.v1";

    public string AccountAppliedRoutingKey { get; init; } =
        "accounts.transfer-applied.v1";

    public string AccountRejectedRoutingKey { get; init; } =
        "accounts.transfer-rejected.v1";

    public string TransferCompletedRoutingKey { get; init; } =
        "transfers.completed.v1";

    public string TransferFailedRoutingKey { get; init; } =
        "transfers.failed.v1";

    public IReadOnlyList<TimeSpan> GetRetryDelays() =>
        RetryDelayParser.Parse(RetryDelaysSeconds);
}

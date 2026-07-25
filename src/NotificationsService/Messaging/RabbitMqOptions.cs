using FinBank.RabbitMqResilience;

namespace FinBank.NotificationsService.Messaging;

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
    public string Queue { get; init; } = "notifications.transfer-completed.v1";
    public string RoutingKey { get; init; } = "transfers.completed.v1";
    public string RetryDelaysSeconds { get; init; } = "5,20,80";
    public int MaxPayloadBytes { get; init; } = 262_144;

    public IReadOnlyList<TimeSpan> GetRetryDelays() =>
        RetryDelayParser.Parse(RetryDelaysSeconds);
}

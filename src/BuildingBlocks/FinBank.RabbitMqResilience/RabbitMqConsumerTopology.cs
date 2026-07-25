namespace FinBank.RabbitMqResilience;

public sealed record RabbitMqConsumerTopology(
    string Exchange,
    string RetryExchange,
    string DeadLetterExchange,
    string Queue,
    IReadOnlyCollection<string> BindingKeys,
    IReadOnlyList<TimeSpan> RetryDelays)
{
    public string DeadLetterQueue => $"{Queue}.dlq";
    public string DeadLetterRoutingKey => $"{Queue}.dead";
    public string RetryRoutingKey(int attempt) => $"{Queue}.retry.{attempt}";
    public string RetryQueue(int attempt) => $"{Queue}.retry.{attempt}";
}

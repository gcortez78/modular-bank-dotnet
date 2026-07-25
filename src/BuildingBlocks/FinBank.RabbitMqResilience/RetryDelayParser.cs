namespace FinBank.RabbitMqResilience;

public static class RetryDelayParser
{
    public static IReadOnlyList<TimeSpan> Parse(string? csv)
    {
        var source = string.IsNullOrWhiteSpace(csv) ? "5,20,80" : csv;
        var values = source.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var delays = new List<TimeSpan>();

        foreach (var value in values)
        {
            if (!int.TryParse(value, out var seconds) || seconds <= 0)
            {
                throw new InvalidOperationException(
                    $"RetryDelaysSeconds contiene un valor inválido: '{value}'.");
            }

            delays.Add(TimeSpan.FromSeconds(seconds));
        }

        if (delays.Count == 0)
            throw new InvalidOperationException("Debe existir al menos un retry delay.");

        return delays;
    }
}

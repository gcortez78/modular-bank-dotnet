namespace TransfersService.Infrastructure;

public sealed class MonolithBankingException(
    int statusCode,
    string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

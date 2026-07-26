namespace FinBank.IntegrationEvents.Validation;

public sealed class EventContractException : Exception
{
    public EventContractException(string message) : base(message)
    {
    }

    public EventContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents.Contracts;

public sealed record TransferCompletedV1(
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    DateTimeOffset CompletedAtUtc) : IIntegrationEventData
{
    public void Validate()
    {
        EventDataValidation.ValidateTransferCommon(
            TransferId,
            UserId,
            SourceAccountId,
            TargetAccountId,
            Amount,
            Currency,
            Reference);

        if (CompletedAtUtc == default)
            throw new EventContractException("completedAtUtc es obligatorio.");
    }
}

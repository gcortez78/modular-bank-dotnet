using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents.Contracts;

public sealed record TransferFailedV1(
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    string Reason,
    DateTimeOffset FailedAtUtc) : IIntegrationEventData
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

        EventDataValidation.RequiredText(Reason, "reason", 500);

        if (FailedAtUtc == default)
            throw new EventContractException("failedAtUtc es obligatorio.");
    }
}

using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents.Contracts;

public sealed record AccountTransferAppliedV1(
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    DateTimeOffset AppliedAtUtc) : IIntegrationEventData
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

        if (AppliedAtUtc == default)
            throw new EventContractException("appliedAtUtc es obligatorio.");
    }
}

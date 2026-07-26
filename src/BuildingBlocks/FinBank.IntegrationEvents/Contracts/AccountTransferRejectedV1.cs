using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents.Contracts;

public sealed record AccountTransferRejectedV1(
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference,
    string Reason,
    DateTimeOffset RejectedAtUtc) : IIntegrationEventData
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

        if (RejectedAtUtc == default)
            throw new EventContractException("rejectedAtUtc es obligatorio.");
    }
}

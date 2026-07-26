using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents.Contracts;

public sealed record TransferRequestedV1(
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string Currency,
    string? Reference) : IIntegrationEventData
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
    }
}

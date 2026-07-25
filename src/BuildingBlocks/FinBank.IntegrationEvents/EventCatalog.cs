namespace FinBank.IntegrationEvents;

public static class EventCatalog
{
    public static readonly EventDefinition TransferRequestedV1 = new(
        "com.finbank.transfers.transfer-requested.v1",
        "transfers.requested.v1",
        "urn:finbank:schema:transfer-requested:v1");

    public static readonly EventDefinition AccountTransferAppliedV1 = new(
        "com.finbank.accounts.transfer-applied.v1",
        "accounts.transfer-applied.v1",
        "urn:finbank:schema:account-transfer-applied:v1");

    public static readonly EventDefinition AccountTransferRejectedV1 = new(
        "com.finbank.accounts.transfer-rejected.v1",
        "accounts.transfer-rejected.v1",
        "urn:finbank:schema:account-transfer-rejected:v1");

    public static readonly EventDefinition TransferCompletedV1 = new(
        "com.finbank.transfers.transfer-completed.v1",
        "transfers.completed.v1",
        "urn:finbank:schema:transfer-completed:v1");

    public static readonly EventDefinition TransferFailedV1 = new(
        "com.finbank.transfers.transfer-failed.v1",
        "transfers.failed.v1",
        "urn:finbank:schema:transfer-failed:v1");
}

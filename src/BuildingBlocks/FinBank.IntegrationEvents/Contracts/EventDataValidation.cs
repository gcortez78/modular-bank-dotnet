using FinBank.IntegrationEvents.Validation;

namespace FinBank.IntegrationEvents.Contracts;

internal static class EventDataValidation
{
    public static void ValidateTransferCommon(
        Guid transferId,
        Guid userId,
        Guid sourceAccountId,
        Guid targetAccountId,
        decimal amount,
        string currency,
        string? reference)
    {
        if (transferId == Guid.Empty)
            throw new EventContractException("transferId es obligatorio.");

        if (userId == Guid.Empty)
            throw new EventContractException("userId es obligatorio.");

        if (sourceAccountId == Guid.Empty || targetAccountId == Guid.Empty)
            throw new EventContractException(
                "sourceAccountId y targetAccountId son obligatorios.");

        if (sourceAccountId == targetAccountId)
            throw new EventContractException(
                "Las cuentas origen y destino deben ser diferentes.");

        if (amount <= 0)
            throw new EventContractException("amount debe ser mayor que cero.");

        RequiredText(currency, "currency", 3, exactLength: true);

        if (reference is { Length: > 200 })
            throw new EventContractException("reference excede 200 caracteres.");
    }

    public static void RequiredText(
        string? value,
        string field,
        int maxLength,
        bool exactLength = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new EventContractException($"{field} es obligatorio.");

        var length = value.Trim().Length;

        if (exactLength && length != maxLength)
        {
            throw new EventContractException(
                $"{field} debe tener exactamente {maxLength} caracteres.");
        }

        if (!exactLength && length > maxLength)
        {
            throw new EventContractException(
                $"{field} excede {maxLength} caracteres.");
        }
    }
}

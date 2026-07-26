using FinBank.NotificationsService.Domain;

namespace FinBank.NotificationsService.Application.Contracts;

public sealed record CreateNotificationRequest(
    Guid UserId,
    NotificationType Type,
    Dictionary<string, string> Payload,
    string? IdempotencyKey);

using FinBank.NotificationsService.Domain;

namespace FinBank.NotificationsService.Application.Contracts;

public sealed record NotificationResponse(
    Guid Id,
    Guid UserId,
    NotificationType Type,
    IReadOnlyDictionary<string, string> Payload,
    DateTime CreatedAt);

public sealed record CreateNotificationResult(
    NotificationResponse Notification,
    bool Created);

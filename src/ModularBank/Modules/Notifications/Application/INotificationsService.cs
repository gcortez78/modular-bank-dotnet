using ModularBank.Modules.Notifications.Domain;

namespace ModularBank.Modules.Notifications.Application;

public interface INotificationsService
{
    Task SendAsync(
        Guid userId,
        NotificationType type,
        Dictionary<string, string> payload,
        string? idempotencyKey = null);

    Task<List<Notification>> GetForUserAsync(Guid userId);
}

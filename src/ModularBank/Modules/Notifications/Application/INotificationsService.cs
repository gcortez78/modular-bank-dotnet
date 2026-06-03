using ModularBank.Modules.Notifications.Domain;

namespace ModularBank.Modules.Notifications.Application;

public interface INotificationsService
{
    Task SendAsync(Guid userId, NotificationType type, Dictionary<string, string> payload);
    Task<List<Notification>> GetForUserAsync(Guid userId);
}

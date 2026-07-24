using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Notifications.Application;
using ModularBank.Modules.Notifications.Domain;

namespace ModularBank.Modules.Notifications.Infrastructure;

// Implementación legacy conservada temporalmente para facilitar rollback.
// Ya no se registra en DI después de aplicar ADR-001.
public class NotificationsService(NotificationsDbContext db)
    : INotificationsService
{
    public async Task SendAsync(
        Guid userId,
        NotificationType type,
        Dictionary<string, string> payload,
        string? idempotencyKey = null)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Payload = payload
        });

        await db.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetForUserAsync(Guid userId)
    {
        return await db.Notifications
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .ToListAsync();
    }
}

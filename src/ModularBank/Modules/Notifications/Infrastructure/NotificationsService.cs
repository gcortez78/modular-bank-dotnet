using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Notifications.Application;
using ModularBank.Modules.Notifications.Domain;

namespace ModularBank.Modules.Notifications.Infrastructure;

public class NotificationsService(NotificationsDbContext db) : INotificationsService
{
    public async Task SendAsync(Guid userId, NotificationType type, Dictionary<string, string> payload)
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
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
    }
}

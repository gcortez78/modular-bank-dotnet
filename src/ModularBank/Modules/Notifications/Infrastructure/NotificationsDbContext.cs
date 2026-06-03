using Microsoft.EntityFrameworkCore;

namespace ModularBank.Modules.Notifications.Infrastructure;

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
    }
}

using Microsoft.AspNetCore.Mvc;
using ModularBank.Modules.Notifications.Infrastructure;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace ModularBank.Modules.Notifications.Api;

public static class NotificationsEndpoints
{
    public static void MapNotificationsEndpoints(this WebApplication app)
    {
        app.MapGet("/notifications", async (ClaimsPrincipal user, NotificationsDbContext db) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub")!);
            var notifications = await db.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
            return Results.Ok(notifications);
        }).RequireAuthorization();
    }
}

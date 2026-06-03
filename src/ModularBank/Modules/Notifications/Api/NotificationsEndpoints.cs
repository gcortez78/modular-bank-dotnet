using ModularBank.Modules.Notifications.Application;
using System.Security.Claims;

namespace ModularBank.Modules.Notifications.Api;

public static class NotificationsEndpoints
{
    public static void MapNotificationsEndpoints(this WebApplication app)
    {
        app.MapGet("/notifications", async (ClaimsPrincipal user, INotificationsService notificationsService) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            if (raw is null || !Guid.TryParse(raw, out var userId))
                return Results.Unauthorized();

            var notifications = await notificationsService.GetForUserAsync(userId);
            return Results.Ok(notifications);
        }).RequireAuthorization();
    }
}

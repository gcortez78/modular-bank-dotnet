using ModularBank.Modules.Audit.Application;
using System.Security.Claims;

namespace ModularBank.Modules.Audit.Api;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/audit", async (ClaimsPrincipal user, IAuditService auditService) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            if (raw is null || !Guid.TryParse(raw, out var userId))
                return Results.Unauthorized();

            var entries = await auditService.GetForUserAsync(userId);
            return Results.Ok(entries);
        }).RequireAuthorization();
    }
}

using ModularBank.Modules.Accounts.Application;
using System.Security.Claims;

namespace ModularBank.Modules.Accounts.Api;

public static class AccountsEndpoints
{
    public static void MapAccountsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/accounts").RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal user, IAccountsService service) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            return Results.Ok(await service.FindByOwnerAsync(userId));
        });

        group.MapPost("", async (ClaimsPrincipal user, IAccountsService service) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var account = await service.CreateAccountAsync(userId);
            return Results.Created($"/accounts/{account.Id}", account);
        });

        group.MapGet("/{id:guid}/balance", async (Guid id, IAccountsService service) =>
        {
            try
            {
                var balance = await service.GetBalanceAsync(id);
                return Results.Ok(new { amount = balance.Amount.ToString() });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }
}

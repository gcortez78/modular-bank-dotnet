using ModularBank.Modules.Accounts.Application;
using ModularBank.Modules.Transfers.Application;
using ModularBank.Modules.Transfers.Application.Dto;
using System.Security.Claims;

namespace ModularBank.Modules.Transfers.Api;

public static class TransfersEndpoints
{
    public static void MapTransfersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/transfers").RequireAuthorization();

        group.MapPost("", async (TransferRequest request, ClaimsPrincipal user, TransferUseCase useCase) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (raw is null || !Guid.TryParse(raw, out var userId))
                return Results.Unauthorized();

            try
            {
                var transfer = await useCase.ExecuteAsync(userId, request);
                return Results.Created($"/transfers/{transfer.Id}", transfer);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.UnprocessableEntity(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        });

        group.MapGet("", async (Guid accountId, ClaimsPrincipal user, TransferUseCase useCase, IAccountsService accountsService) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (raw is null || !Guid.TryParse(raw, out var userId))
                return Results.Unauthorized();

            var owned = await accountsService.FindByOwnerAsync(userId);
            if (!owned.Any(a => a.Id == accountId))
                return Results.Forbid();

            return Results.Ok(await useCase.GetHistoryAsync(accountId));
        });
    }
}

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

        group.MapGet("", async (Guid accountId, ClaimsPrincipal user, TransferUseCase useCase) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (raw is null || !Guid.TryParse(raw, out var userId))
                return Results.Unauthorized();
            try
            {
                return Results.Ok(await useCase.GetHistoryAsync(userId, accountId));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        });
    }
}

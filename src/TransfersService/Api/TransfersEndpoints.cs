using System.Security.Claims;
using TransfersService.Application;
using TransfersService.Application.Contracts;

namespace TransfersService.Api;

public static class TransfersEndpoints
{
    public static IEndpointRouteBuilder MapTransfersEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transfers")
            .RequireAuthorization()
            .WithTags("Transfers");

        group.MapPost("", CreateTransferAsync);
        group.MapGet("", ListTransfersAsync);

        return app;
    }

    private static async Task<IResult> CreateTransferAsync(
        ClaimsPrincipal user,
        CreateTransferRequest request,
        ITransfersService transfersService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null)
            return Results.Unauthorized();

        try
        {
            var transfer = await transfersService.CreateAsync(
                userId.Value,
                request,
                cancellationToken);

            return Results.Accepted(
                $"/transfers/{transfer.Id}",
                transfer);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
    }

    private static async Task<IResult> ListTransfersAsync(
        ClaimsPrincipal user,
        ITransfersService transfersService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null)
            return Results.Unauthorized();

        var transfers = await transfersService.ListAsync(
            userId.Value,
            cancellationToken);

        return Results.Ok(transfers);
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                    user.FindFirstValue("sub");

        return Guid.TryParse(value, out var userId)
            ? userId
            : null;
    }
}

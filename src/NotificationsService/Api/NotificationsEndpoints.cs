using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinBank.NotificationsService.Application;
using FinBank.NotificationsService.Application.Contracts;

namespace FinBank.NotificationsService.Api;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var publicGroup = endpoints
            .MapGroup("/notifications")
            .RequireAuthorization();

        publicGroup.MapGet("", GetForAuthenticatedUser);

        var internalGroup = endpoints
            .MapGroup("/internal/notifications")
            .AddEndpointFilter<InternalApiKeyFilter>();

        internalGroup.MapPost("", Create);
        internalGroup.MapGet("/{userId:guid}", GetForUserInternal);

        return endpoints;
    }

    private static async Task<IResult> GetForAuthenticatedUser(
        ClaimsPrincipal user,
        INotificationsService service,
        CancellationToken cancellationToken)
    {
        var rawUserId =
            user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(rawUserId, out var userId))
        {
            return Results.Unauthorized();
        }

        var notifications = await service.GetForUserAsync(userId, cancellationToken);
        return Results.Ok(notifications);
    }

    private static async Task<IResult> GetForUserInternal(
        Guid userId,
        INotificationsService service,
        CancellationToken cancellationToken)
    {
        var notifications = await service.GetForUserAsync(userId, cancellationToken);
        return Results.Ok(notifications);
    }

    private static async Task<IResult> Create(
        CreateNotificationRequest request,
        INotificationsService service,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "UserId es obligatorio." });
        }

        if (!Enum.IsDefined(request.Type))
        {
            return Results.BadRequest(new { error = "NotificationType no es válido." });
        }

        if (request.Payload is null)
        {
            return Results.BadRequest(new { error = "Payload es obligatorio." });
        }

        if (request.IdempotencyKey is { Length: > 100 })
        {
            return Results.BadRequest(new
            {
                error = "IdempotencyKey no puede superar 100 caracteres."
            });
        }

        var result = await service.CreateAsync(request, cancellationToken);

        if (result.Created)
        {
            return Results.Created(
                $"/internal/notifications/{result.Notification.Id}",
                result.Notification);
        }

        return Results.Ok(result.Notification);
    }
}

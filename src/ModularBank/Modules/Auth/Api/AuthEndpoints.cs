using ModularBank.Modules.Auth.Application;
using ModularBank.Modules.Auth.Application.Dto;

namespace ModularBank.Modules.Auth.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/register", async (RegisterRequest request, AuthUseCase useCase) =>
        {
            try
            {
                var response = await useCase.RegisterAsync(request);
                return Results.Created("/auth/me", response);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        });

        group.MapPost("/login", async (LoginRequest request, AuthUseCase useCase) =>
        {
            try
            {
                var response = await useCase.LoginAsync(request);
                return Results.Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        });

        group.MapPost("/refresh", async (string refreshToken, AuthUseCase useCase) =>
        {
            try
            {
                var response = await useCase.RefreshAsync(refreshToken);
                return Results.Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        });
    }
}

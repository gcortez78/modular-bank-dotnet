using ModularBank.Modules.Auth.Application.Dto;
using ModularBank.Modules.Auth.Domain;
using ModularBank.Modules.Auth.Infrastructure;
using ModularBank.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ModularBank.Modules.Auth.Application;

public class AuthUseCase(AuthDbContext db, JwtUtil jwtUtil, IConfiguration config)
{
    private readonly int _refreshExpirationDays = int.Parse(
        config["Jwt:RefreshExpirationDays"]
        ?? throw new InvalidOperationException("Jwt:RefreshExpirationDays is not configured."));

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await db.Users.AnyAsync(u => u.Email == request.Email))
            throw new InvalidOperationException("Email already registered");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Name = request.Name
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return await BuildAuthResponseAsync(user.Id);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new UnauthorizedAccessException("Invalid credentials");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials");

        return await BuildAuthResponseAsync(user.Id);
    }

    private static string HashToken(string raw) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw)));

    public async Task<AuthResponse> RefreshAsync(string rawToken)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == HashToken(rawToken))
            ?? throw new UnauthorizedAccessException("Invalid refresh token");

        if (token.ExpiresAt < DateTime.UtcNow)
        {
            db.RefreshTokens.Remove(token);
            await db.SaveChangesAsync();
            throw new UnauthorizedAccessException("Refresh token expired");
        }

        db.RefreshTokens.Remove(token);
        await db.SaveChangesAsync();
        return await BuildAuthResponseAsync(token.UserId);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(Guid userId)
    {
        var accessToken = jwtUtil.GenerateAccessToken(userId);
        var rawRefreshToken = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = HashToken(rawRefreshToken),   // store hash
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshExpirationDays)
        });
        await db.SaveChangesAsync();

        return new AuthResponse(accessToken, rawRefreshToken);
    }
}

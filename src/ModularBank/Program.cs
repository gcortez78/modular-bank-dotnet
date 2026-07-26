using ModularBank.Modules.Auth.Infrastructure;
using ModularBank.Modules.Auth.Api;
using ModularBank.Modules.Accounts.Infrastructure;
using ModularBank.Modules.Accounts.Api;
using ModularBank.Modules.Transfers.Api;
using ModularBank.Modules.Notifications.Infrastructure;
using ModularBank.Modules.Audit.Infrastructure;
using ModularBank.Modules.Audit.Api;
using ModularBank.Shared.Infrastructure;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ModularBank.Messaging;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured.");

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret must be configured and at least 32 characters.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<JwtUtil>();

builder.Services.AddAuthModule(connectionString);
builder.Services.AddAccountsModule(connectionString);

// ADR-001: la interfaz permanece en el monolito, pero la implementaciÃ³n
// ahora es un cliente HTTP hacia Notifications Service.
builder.Services.AddNotificationsModule(builder.Configuration);

builder.Services.AddAuditModule(connectionString);
builder.Services.AddSagaMessaging(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var contexts = new DbContext[]
    {
        scope.ServiceProvider.GetRequiredService<AuthDbContext>(),
        scope.ServiceProvider.GetRequiredService<AccountsDbContext>(),
        scope.ServiceProvider.GetRequiredService<AuditDbContext>()
    };

    foreach (var context in contexts)
    {
        context.Database.Migrate();
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");

app.MapAuthEndpoints();
app.MapAccountsEndpoints();

// ADR-001: /notifications ya no se publica desde el monolito.
// YARP envÃ­a esa ruta al microservicio extraÃ­do.

app.MapAuditEndpoints();

app.Run();

public partial class Program;



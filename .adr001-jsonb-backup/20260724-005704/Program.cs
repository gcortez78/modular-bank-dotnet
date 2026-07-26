using System.Text;
using FinBank.NotificationsService.Api;
using FinBank.NotificationsService.Application;
using FinBank.NotificationsService.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default no está configurada.");

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret debe configurarse y tener al menos 32 caracteres.");
}

var internalApiKey = builder.Configuration["InternalApiKey"];
if (string.IsNullOrWhiteSpace(internalApiKey) || internalApiKey.Length < 32)
{
    throw new InvalidOperationException(
        "InternalApiKey debe configurarse y tener al menos 32 caracteres.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
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

var dataSource = new NpgsqlDataSourceBuilder(connectionString)
    .EnableDynamicJson()
    .Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<NotificationsDbContext>(
    options => options.UseNpgsql(dataSource));

builder.Services.AddScoped<INotificationsService, PostgresNotificationsService>();
builder.Services.AddScoped<InternalApiKeyFilter>();

var app = builder.Build();

await ApplyMigrationsAsync(app);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
        "/health",
        async (NotificationsDbContext db, CancellationToken cancellationToken) =>
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? Results.Ok(new { status = "ok", database = "connected" })
                : Results.Problem(
                    title: "Unhealthy",
                    detail: "No existe conexión con PostgreSQL.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        })
    .AllowAnonymous();

app.MapNotificationsEndpoints();

app.Run();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    const int maxAttempts = 10;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();

            await db.Database.MigrateAsync();
            return;
        }
        catch (Exception exception) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(
                exception,
                "No se pudieron aplicar migraciones de Notifications. Intento {Attempt}/{MaxAttempts}.",
                attempt,
                maxAttempts);

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    await using var finalScope = app.Services.CreateAsyncScope();
    var finalDb = finalScope.ServiceProvider
        .GetRequiredService<NotificationsDbContext>();

    await finalDb.Database.MigrateAsync();
}

public partial class Program { }

using System.Text;
using FinBank.NotificationsService.Api;
using FinBank.NotificationsService.Application;
using FinBank.NotificationsService.Infrastructure;
using FinBank.NotificationsService.Messaging;
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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "notifications")));

builder.Services.AddScoped<INotificationsService, PostgresNotificationsService>();
builder.Services.AddScoped<InternalApiKeyFilter>();

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddHostedService<TransferCompletedConsumer>();

var app = builder.Build();

await EnsureSchemaAndApplyMigrationsAsync(app, connectionString);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
    "/health",
    async (
        NotificationsDbContext db,
        CancellationToken cancellationToken) =>
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? Results.Ok(new
            {
                status = "ok",
                service = "notifications-service",
                database = "connected"
            })
            : Results.Problem(
                title: "Unhealthy",
                detail: "No existe conexión con PostgreSQL.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous();

app.MapNotificationsEndpoints();

app.Run();

static async Task EnsureSchemaAndApplyMigrationsAsync(
    WebApplication app,
    string connectionString)
{
    const int maxAttempts = 10;
    var logger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("NotificationsService.Migrations");

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE SCHEMA IF NOT EXISTS notifications AUTHORIZATION CURRENT_USER;";
                await command.ExecuteNonQueryAsync();
            }

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();

            await db.Database.MigrateAsync();

            logger.LogInformation(
                "Migraciones de Notifications aplicadas correctamente.");

            return;
        }
        catch (NpgsqlException ex) when (ex.IsTransient && attempt < maxAttempts)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 15));

            logger.LogWarning(
                ex,
                "PostgreSQL de Notifications no está disponible. Intento {Attempt}/{MaxAttempts}; reintento en {Delay}.",
                attempt,
                maxAttempts,
                delay);

            await Task.Delay(delay);
        }
        catch (Exception ex)
        {
            logger.LogCritical(
                ex,
                "Error permanente aplicando migraciones de Notifications.");

            throw;
        }
    }
}

public partial class Program;

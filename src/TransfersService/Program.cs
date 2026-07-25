using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TransfersService.Api;
using TransfersService.Application;
using TransfersService.Infrastructure;
using TransfersService.Messaging;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "No se configuró ConnectionStrings:Default.");

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("No se configuró Jwt:Secret.");

if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException(
        "Jwt:Secret debe tener al menos 32 bytes.");

builder.Services.AddDbContext<TransfersDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "transfers")));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.Configure<OutboxOptions>(
    builder.Configuration.GetSection(OutboxOptions.SectionName));

builder.Services.AddScoped<ITransfersService, TransfersApplicationService>();
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<AccountTransferResultConsumer>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

await EnsureSchemaAndApplyMigrationsAsync(app, connectionString);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
    "/health",
    async (
        TransfersDbContext db,
        CancellationToken cancellationToken) =>
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? Results.Ok(new
            {
                status = "ok",
                service = "transfers-service",
                database = "connected"
            })
            : Results.Problem(
                title: "Unhealthy",
                detail: "No existe conexión con PostgreSQL.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous();

app.MapTransfersEndpoints();

app.Run();

static async Task EnsureSchemaAndApplyMigrationsAsync(
    WebApplication app,
    string connectionString)
{
    const int maxAttempts = 10;
    var logger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("TransfersService.Migrations");

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE SCHEMA IF NOT EXISTS transfers AUTHORIZATION CURRENT_USER;";
                await command.ExecuteNonQueryAsync();
            }

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider
                .GetRequiredService<TransfersDbContext>();

            await db.Database.MigrateAsync();

            logger.LogInformation(
                "Migraciones de Transfers aplicadas correctamente.");

            return;
        }
        catch (NpgsqlException ex) when (ex.IsTransient && attempt < maxAttempts)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 15));

            logger.LogWarning(
                ex,
                "PostgreSQL de Transfers no está disponible. Intento {Attempt}/{MaxAttempts}; reintento en {Delay}.",
                attempt,
                maxAttempts,
                delay);

            await Task.Delay(delay);
        }
        catch (Exception ex)
        {
            logger.LogCritical(
                ex,
                "Error permanente aplicando migraciones de Transfers.");

            throw;
        }
    }
}

public partial class Program;

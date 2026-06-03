using ModularBank.Modules.Auth.Infrastructure;
using ModularBank.Modules.Auth.Api;
using ModularBank.Modules.Accounts.Infrastructure;
using ModularBank.Modules.Transfers.Infrastructure;
using ModularBank.Modules.Notifications.Infrastructure;
using ModularBank.Modules.Audit.Infrastructure;
using ModularBank.Shared.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be configured and at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateIssuer = false,   // TODO: set ValidIssuer before microservice extraction
            ValidateAudience = false, // TODO: set ValidAudience before microservice extraction
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<JwtUtil>();

builder.Services.AddAuthModule(connectionString);
builder.Services.AddAccountsModule(connectionString);
builder.Services.AddTransfersModule(connectionString);
builder.Services.AddNotificationsModule(connectionString);
builder.Services.AddAuditModule(connectionString);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");
app.MapAuthEndpoints();

app.Run();

public partial class Program { }

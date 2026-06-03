using ModularBank.Modules.Auth.Infrastructure;
using ModularBank.Modules.Accounts.Infrastructure;
using ModularBank.Modules.Transfers.Infrastructure;
using ModularBank.Modules.Notifications.Infrastructure;
using ModularBank.Modules.Audit.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;
var jwtSecret = builder.Configuration["Jwt:Secret"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddAuthModule(connectionString);
builder.Services.AddAccountsModule(connectionString);
builder.Services.AddTransfersModule(connectionString);
builder.Services.AddNotificationsModule(connectionString);
builder.Services.AddAuditModule(connectionString);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");

app.Run();

public partial class Program { }

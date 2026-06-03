using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Auth.Infrastructure;
using ModularBank.Modules.Accounts.Infrastructure;
using ModularBank.Modules.Transfers.Infrastructure;
using ModularBank.Modules.Notifications.Infrastructure;
using ModularBank.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ModularBank.Tests;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("modular_bank")
        .WithUsername("bank")
        .WithPassword("bank")
        .WithImage("postgres:16")
        .Build();

    protected HttpClient Client { get; private set; } = null!;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
                builder.UseSetting("Jwt:Secret", "test-secret-for-integration-tests-min-32chars!!");
            });

        Client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var contexts = new DbContext[]
        {
            scope.ServiceProvider.GetRequiredService<AuthDbContext>(),
            scope.ServiceProvider.GetRequiredService<AccountsDbContext>(),
            scope.ServiceProvider.GetRequiredService<TransfersDbContext>(),
            scope.ServiceProvider.GetRequiredService<NotificationsDbContext>(),
            scope.ServiceProvider.GetRequiredService<AuditDbContext>()
        };
        foreach (var ctx in contexts)
            await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory != null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

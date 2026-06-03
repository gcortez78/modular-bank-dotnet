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

// Singleton container pattern: one Postgres container for the entire test suite JVM lifetime.
// Prevents port-reuse timing issues with Colima's Docker NAT when spinning up per-test containers.
public sealed class SharedPostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("modular_bank")
        .WithUsername("bank")
        .WithPassword("bank")
        .WithImage("postgres:16")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<SharedPostgresContainer> { }

[Collection("IntegrationTests")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly SharedPostgresContainer _sharedDb;
    private WebApplicationFactory<Program>? _factory;

    protected HttpClient Client { get; private set; } = null!;
    protected WebApplicationFactory<Program> Factory => _factory!;

    protected IntegrationTestBase(SharedPostgresContainer sharedDb)
    {
        _sharedDb = sharedDb;
    }

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Default", _sharedDb.ConnectionString);
                builder.UseSetting("Jwt:Secret", "test-secret-for-integration-tests-min-32chars!!");
                builder.UseSetting("Jwt:AccessExpirationMinutes", "15");
                builder.UseSetting("Jwt:RefreshExpirationDays", "7");
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
    }
}

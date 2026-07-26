using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModularBank.Modules.Accounts.Infrastructure;

public sealed class AccountsDbContextFactory
    : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__Default")
            ?? "Host=localhost;Port=5433;Database=modular_bank;Username=bank;Password=bank-local";

        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AccountsDbContext(options);
    }
}

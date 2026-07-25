using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModularBank.Modules.Accounts.Infrastructure;

public sealed class AccountsDbContextFactory
    : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5433;Database=modular_bank;Username=bank;Password=bank-local";

        var optionsBuilder =
            new DbContextOptionsBuilder<AccountsDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new AccountsDbContext(optionsBuilder.Options);
    }
}

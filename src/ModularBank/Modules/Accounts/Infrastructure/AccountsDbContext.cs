using Microsoft.EntityFrameworkCore;

namespace ModularBank.Modules.Accounts.Infrastructure;

public class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("accounts");
    }
}

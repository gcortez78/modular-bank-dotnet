using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Accounts.Domain;

namespace ModularBank.Modules.Accounts.Infrastructure;

public class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("accounts");

        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.AccountNumber).HasColumnName("account_number")
                .HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.AccountNumber).IsUnique();
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_accounts_user_id");
            e.Property(x => x.Balance).HasColumnType("numeric(19,4)").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        });
    }
}

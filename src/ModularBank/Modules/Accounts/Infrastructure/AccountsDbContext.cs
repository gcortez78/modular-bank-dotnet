using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Accounts.Domain;

namespace ModularBank.Modules.Accounts.Infrastructure;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ProcessedTransferCommand> ProcessedTransferCommands =>
        Set<ProcessedTransferCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("accounts");

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Balance).HasPrecision(18, 2);
            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<ProcessedTransferCommand>(entity =>
        {
            entity.ToTable("processed_transfer_commands");
            entity.HasKey(x => x.TransferId)
                .HasName("pk_processed_transfer_commands");

            entity.Property(x => x.TransferId).HasColumnName("transfer_id");
            entity.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(x => x.SourceAccountId).HasColumnName("source_account_id").IsRequired();
            entity.Property(x => x.TargetAccountId).HasColumnName("target_account_id").IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200);
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at").IsRequired();

            entity.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName("ix_processed_transfer_commands_processed_at");
        });
    }
}

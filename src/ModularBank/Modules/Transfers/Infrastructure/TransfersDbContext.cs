using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Transfers.Domain;

namespace ModularBank.Modules.Transfers.Infrastructure;

public class TransfersDbContext(DbContextOptions<TransfersDbContext> options) : DbContext(options)
{
    public DbSet<Transfer> Transfers => Set<Transfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("transfers");

        modelBuilder.Entity<Transfer>(e =>
        {
            e.ToTable("transfers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SourceAccountId).HasColumnName("source_account_id").IsRequired();
            e.Property(x => x.TargetAccountId).HasColumnName("target_account_id").IsRequired();
            e.Property(x => x.Amount).HasColumnType("numeric(19,4)").IsRequired();
            e.Property(x => x.Reference).HasColumnName("reference");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .ValueGeneratedOnAdd().HasDefaultValueSql("now()");
            e.HasIndex(x => x.SourceAccountId).HasDatabaseName("ix_transfers_source_account_id");
            e.HasIndex(x => x.TargetAccountId).HasDatabaseName("ix_transfers_target_account_id");
        });
    }
}

using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Audit.Domain;

namespace ModularBank.Modules.Audit.Infrastructure;

public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("audit");

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            e.Property(x => x.Metadata)
                .HasColumnName("metadata")
                .HasColumnType("jsonb")
                .IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .ValueGeneratedOnAdd().HasDefaultValueSql("now()");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_audit_entries_user_id");
        });
    }
}

using FinBank.NotificationsService.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinBank.NotificationsService.Infrastructure;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
    : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(x => x.Type)
                .HasColumnName("type")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.PayloadJson)
                .HasColumnName("payload")
                .HasColumnType("jsonb")
                .IsRequired();

            entity.Property(x => x.IdempotencyKey)
                .HasColumnName("idempotency_key")
                .HasMaxLength(100);

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(x => new { x.UserId, x.CreatedAt })
                .HasDatabaseName("ix_notifications_user_created_at");

            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasFilter("\"idempotency_key\" IS NOT NULL")
                .HasDatabaseName("ux_notifications_idempotency_key");
        });
    }
}

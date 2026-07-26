using Microsoft.EntityFrameworkCore;
using TransfersService.Domain;

namespace TransfersService.Infrastructure;

public sealed class TransfersDbContext(DbContextOptions<TransfersDbContext> options)
    : DbContext(options)
{
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents =>
        Set<ProcessedIntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("transfers");

        modelBuilder.Entity<Transfer>(entity =>
        {
            entity.ToTable("transfers");
            entity.HasKey(x => x.Id).HasName("pk_transfers");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(x => x.SourceAccountId)
                .HasColumnName("source_account_id")
                .IsRequired();
            entity.Property(x => x.TargetAccountId)
                .HasColumnName("target_account_id")
                .IsRequired();
            entity.Property(x => x.Amount)
                .HasColumnName("amount")
                .HasPrecision(18, 2)
                .IsRequired();
            entity.Property(x => x.Reference)
                .HasColumnName("reference")
                .HasMaxLength(200);
            entity.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.Property(x => x.FailureReason)
                .HasColumnName("failure_reason")
                .HasMaxLength(500);

            entity.HasIndex(x => new { x.UserId, x.CreatedAt })
                .HasDatabaseName("ix_transfers_user_created_at");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id).HasName("pk_outbox_messages");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EventType)
                .HasColumnName("event_type")
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(x => x.RoutingKey)
                .HasColumnName("routing_key")
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(x => x.PayloadJson)
                .HasColumnName("payload")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(x => x.OccurredAt)
                .HasColumnName("occurred_at")
                .IsRequired();
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            entity.Property(x => x.Attempts)
                .HasColumnName("attempts")
                .IsRequired();
            entity.Property(x => x.LastError)
                .HasColumnName("last_error")
                .HasMaxLength(2000);

            entity.HasIndex(x => new { x.ProcessedAt, x.OccurredAt })
                .HasDatabaseName("ix_outbox_pending");
        });

        modelBuilder.Entity<ProcessedIntegrationEvent>(entity =>
        {
            entity.ToTable("processed_integration_events");
            entity.HasKey(x => new { x.ConsumerName, x.EventId })
                .HasName("pk_processed_integration_events");

            entity.Property(x => x.EventId).HasColumnName("event_id");
            entity.Property(x => x.ConsumerName)
                .HasColumnName("consumer_name")
                .HasMaxLength(200);
            entity.Property(x => x.ProcessedAt)
                .HasColumnName("processed_at")
                .IsRequired();

            entity.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName("ix_processed_integration_events_processed_at");
        });
    }
}

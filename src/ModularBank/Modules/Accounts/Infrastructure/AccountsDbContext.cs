using Microsoft.EntityFrameworkCore;
using ModularBank.Messaging;
using ModularBank.Modules.Accounts.Domain;

namespace ModularBank.Modules.Accounts.Infrastructure;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<ProcessedTransferCommand> ProcessedTransferCommands =>
        Set<ProcessedTransferCommand>();

    public DbSet<AccountOutboxMessage> AccountOutboxMessages =>
        Set<AccountOutboxMessage>();

    public DbSet<SagaInboxMessage> SagaInboxMessages =>
        Set<SagaInboxMessage>();

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
            entity.Property(x => x.RequestEventId)
                .HasColumnName("request_event_id");
            entity.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired();
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
            entity.Property(x => x.Result)
                .HasColumnName("result")
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(x => x.FailureReason)
                .HasColumnName("failure_reason")
                .HasMaxLength(500);
            entity.Property(x => x.ProcessedAt)
                .HasColumnName("processed_at")
                .IsRequired();

            entity.HasIndex(x => x.RequestEventId)
                .IsUnique()
                .HasDatabaseName(
                    "ux_processed_transfer_commands_request_event_id");

            entity.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName(
                    "ix_processed_transfer_commands_processed_at");
        });

        modelBuilder.Entity<AccountOutboxMessage>(entity =>
        {
            entity.ToTable("account_outbox_messages");
            entity.HasKey(x => x.Id)
                .HasName("pk_account_outbox_messages");

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
            entity.Property(x => x.ProcessedAt)
                .HasColumnName("processed_at");
            entity.Property(x => x.Attempts)
                .HasColumnName("attempts")
                .IsRequired();
            entity.Property(x => x.LastError)
                .HasColumnName("last_error")
                .HasMaxLength(2000);

            entity.HasIndex(x => new { x.ProcessedAt, x.OccurredAt })
                .HasDatabaseName("ix_account_outbox_pending");
        });

        modelBuilder.Entity<SagaInboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages", "integration");
            entity.HasKey(x => new { x.ConsumerName, x.EventId })
                .HasName("pk_integration_inbox_messages");

            entity.Property(x => x.ConsumerName)
                .HasColumnName("consumer_name")
                .HasMaxLength(200);
            entity.Property(x => x.EventId)
                .HasColumnName("event_id");
            entity.Property(x => x.ProcessedAt)
                .HasColumnName("processed_at")
                .IsRequired();

            entity.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName(
                    "ix_integration_inbox_messages_processed_at");
        });
    }
}

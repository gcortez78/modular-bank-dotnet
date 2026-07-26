using System.Collections.Generic;
using FinBank.NotificationsService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FinBank.NotificationsService.Migrations;

[DbContext(typeof(NotificationsDbContext))]
partial class NotificationsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema("notifications")
            .HasAnnotation("ProductVersion", "10.0.0");

        modelBuilder.Entity(
            "FinBank.NotificationsService.Domain.Notification",
            entity =>
            {
                entity.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                entity.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                entity.Property<string>("IdempotencyKey")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)")
                    .HasColumnName("idempotency_key");

                entity.Property<Dictionary<string, string>>("Payload")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("payload");

                entity.Property<int>("Type")
                    .HasColumnType("integer")
                    .HasColumnName("type");

                entity.Property<Guid>("UserId")
                    .HasColumnType("uuid")
                    .HasColumnName("user_id");

                entity.HasKey("Id");

                entity.HasIndex("IdempotencyKey")
                    .IsUnique()
                    .HasDatabaseName("ux_notifications_idempotency_key")
                    .HasFilter("\"idempotency_key\" IS NOT NULL");

                entity.HasIndex("UserId", "CreatedAt")
                    .HasDatabaseName("ix_notifications_user_created_at");

                entity.ToTable("notifications", "notifications");
            });
    }
}

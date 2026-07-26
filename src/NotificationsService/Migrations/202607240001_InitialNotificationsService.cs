using FinBank.NotificationsService.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinBank.NotificationsService.Migrations;

[DbContext(typeof(NotificationsDbContext))]
[Migration("202607240001_InitialNotificationsService")]
public partial class InitialNotificationsService : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "notifications");

        migrationBuilder.CreateTable(
            name: "notifications",
            schema: "notifications",
            columns: table => new
            {
                id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                user_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                type = table.Column<int>(
                    type: "integer",
                    nullable: false),
                payload = table.Column<string>(
                    type: "jsonb",
                    nullable: false),
                idempotency_key = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: true),
                created_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notifications", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_notifications_user_created_at",
            schema: "notifications",
            table: "notifications",
            columns: new[] { "user_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ux_notifications_idempotency_key",
            schema: "notifications",
            table: "notifications",
            column: "idempotency_key",
            unique: true,
            filter: "\"idempotency_key\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notifications",
            schema: "notifications");
    }
}

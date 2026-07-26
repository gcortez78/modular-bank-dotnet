using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransfersService.Infrastructure;

#nullable disable

namespace TransfersService.Migrations;

[DbContext(typeof(TransfersDbContext))]
[Migration("202607250001_InitialTransfersService")]
public partial class InitialTransfersService : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "transfers");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                routing_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "transfers",
            schema: "transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_transfers", x => x.id);
                table.CheckConstraint("ck_transfers_amount_positive", "amount > 0");
            });

        migrationBuilder.CreateIndex(
            name: "ix_outbox_pending",
            schema: "transfers",
            table: "outbox_messages",
            columns: new[] { "processed_at", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_transfers_user_created_at",
            schema: "transfers",
            table: "transfers",
            columns: new[] { "user_id", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "transfers");
        migrationBuilder.DropTable(name: "transfers", schema: "transfers");
    }
}

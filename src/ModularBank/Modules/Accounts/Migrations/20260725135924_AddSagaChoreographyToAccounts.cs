using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularBank.Modules.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaChoreographyToAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "accounts",
                table: "processed_transfer_commands",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "request_event_id",
                schema: "accounts",
                table: "processed_transfer_commands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result",
                schema: "accounts",
                table: "processed_transfer_commands",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "account_outbox_messages",
                schema: "accounts",
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
                    table.PrimaryKey("pk_account_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "integration",
                columns: table => new
                {
                    consumer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_inbox_messages", x => new { x.consumer_name, x.event_id });
                });

            migrationBuilder.CreateIndex(
                name: "ux_processed_transfer_commands_request_event_id",
                schema: "accounts",
                table: "processed_transfer_commands",
                column: "request_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_outbox_pending",
                schema: "accounts",
                table: "account_outbox_messages",
                columns: new[] { "processed_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_inbox_messages_processed_at",
                schema: "integration",
                table: "inbox_messages",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_outbox_messages",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "integration");

            migrationBuilder.DropIndex(
                name: "ux_processed_transfer_commands_request_event_id",
                schema: "accounts",
                table: "processed_transfer_commands");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "accounts",
                table: "processed_transfer_commands");

            migrationBuilder.DropColumn(
                name: "request_event_id",
                schema: "accounts",
                table: "processed_transfer_commands");

            migrationBuilder.DropColumn(
                name: "result",
                schema: "accounts",
                table: "processed_transfer_commands");
        }
    }
}

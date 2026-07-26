using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularBank.Modules.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedTransferCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_account_number",
                schema: "accounts",
                table: "accounts");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "accounts",
                table: "accounts",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "accounts",
                table: "accounts",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "account_number",
                schema: "accounts",
                table: "accounts",
                newName: "AccountNumber");

            migrationBuilder.RenameIndex(
                name: "ix_accounts_user_id",
                schema: "accounts",
                table: "accounts",
                newName: "IX_accounts_UserId");

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance",
                schema: "accounts",
                table: "accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "accounts",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<string>(
                name: "AccountNumber",
                schema: "accounts",
                table: "accounts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateTable(
                name: "processed_transfer_commands",
                schema: "accounts",
                columns: table => new
                {
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_transfer_commands", x => x.transfer_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_transfer_commands_processed_at",
                schema: "accounts",
                table: "processed_transfer_commands",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_transfer_commands",
                schema: "accounts");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "accounts",
                table: "accounts",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "accounts",
                table: "accounts",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "AccountNumber",
                schema: "accounts",
                table: "accounts",
                newName: "account_number");

            migrationBuilder.RenameIndex(
                name: "IX_accounts_UserId",
                schema: "accounts",
                table: "accounts",
                newName: "ix_accounts_user_id");

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance",
                schema: "accounts",
                table: "accounts",
                type: "numeric(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                schema: "accounts",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "account_number",
                schema: "accounts",
                table: "accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_account_number",
                schema: "accounts",
                table: "accounts",
                column: "account_number",
                unique: true);
        }
    }
}

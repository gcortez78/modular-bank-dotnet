using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularBank.Modules.Transfers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transfers");

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_source_account_id",
                schema: "transfers",
                table: "transfers",
                column: "source_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_target_account_id",
                schema: "transfers",
                table: "transfers",
                column: "target_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfers",
                schema: "transfers");
        }
    }
}

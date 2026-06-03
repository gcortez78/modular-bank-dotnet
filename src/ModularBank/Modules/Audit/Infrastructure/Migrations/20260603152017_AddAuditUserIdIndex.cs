using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularBank.Modules.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditUserIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_user_id",
                schema: "audit",
                table: "audit_entries",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_entries_user_id",
                schema: "audit",
                table: "audit_entries");
        }
    }
}

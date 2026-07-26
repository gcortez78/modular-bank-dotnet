using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransfersService.Infrastructure;

#nullable disable

namespace TransfersService.Migrations;

[DbContext(typeof(TransfersDbContext))]
[Migration("202607250002_AddSagaChoreography")]
public partial class AddSagaChoreography : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "processed_integration_events",
            schema: "transfers",
            columns: table => new
            {
                consumer_name = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false),
                event_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                processed_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_processed_integration_events",
                    x => new
                    {
                        x.consumer_name,
                        x.event_id
                    });
            });

        migrationBuilder.CreateIndex(
            name: "ix_processed_integration_events_processed_at",
            schema: "transfers",
            table: "processed_integration_events",
            column: "processed_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "processed_integration_events",
            schema: "transfers");
    }
}

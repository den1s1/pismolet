using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pismolet.Web.Infrastructure.Database;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

[DbContext(typeof(PismoletDbContext))]
[Migration("20260621194000_AddSendEventsAcceptedUtcDay")]
public partial class AddSendEventsAcceptedUtcDay : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AcceptedUtcDay",
            table: "send_events",
            type: "integer",
            nullable: true);

        if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            migrationBuilder.Sql("UPDATE send_events SET \"AcceptedUtcDay\" = ((EXTRACT(YEAR FROM \"AcceptedAt\" AT TIME ZONE 'UTC')::int * 10000) + (EXTRACT(MONTH FROM \"AcceptedAt\" AT TIME ZONE 'UTC')::int * 100) + EXTRACT(DAY FROM \"AcceptedAt\" AT TIME ZONE 'UTC')::int) WHERE \"AcceptedAt\" IS NOT NULL AND \"AcceptedUtcDay\" IS NULL;");
        }
        else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            migrationBuilder.Sql("UPDATE send_events SET \"AcceptedUtcDay\" = CAST(strftime('%Y%m%d', \"AcceptedAt\") AS INTEGER) WHERE \"AcceptedAt\" IS NOT NULL AND \"AcceptedUtcDay\" IS NULL;");
        }

        migrationBuilder.CreateIndex(
            name: "IX_send_events_OwnerEmail_AcceptedUtcDay",
            table: "send_events",
            columns: new[] { "OwnerEmail", "AcceptedUtcDay" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_send_events_OwnerEmail_AcceptedUtcDay",
            table: "send_events");

        migrationBuilder.DropColumn(
            name: "AcceptedUtcDay",
            table: "send_events");
    }
}

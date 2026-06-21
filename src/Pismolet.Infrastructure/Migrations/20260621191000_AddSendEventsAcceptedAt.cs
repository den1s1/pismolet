using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

public partial class AddSendEventsAcceptedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AcceptedAt",
            table: "send_events",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("UPDATE send_events SET \"AcceptedAt\" = \"UpdatedAt\" WHERE \"Status\" = 'Accepted' AND \"AcceptedAt\" IS NULL;");

        migrationBuilder.CreateIndex(
            name: "IX_send_events_OwnerEmail_AcceptedAt",
            table: "send_events",
            columns: new[] { "OwnerEmail", "AcceptedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_send_events_OwnerEmail_AcceptedAt",
            table: "send_events");

        migrationBuilder.DropColumn(
            name: "AcceptedAt",
            table: "send_events");
    }
}

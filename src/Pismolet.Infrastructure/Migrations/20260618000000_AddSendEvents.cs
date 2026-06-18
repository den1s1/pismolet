using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

public partial class AddSendEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "send_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                RecipientEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ProviderMessageId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                Attempt = table.Column<int>(type: "integer", nullable: false),
                ErrorCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_send_events", x => x.Id);
                table.ForeignKey(
                    name: "FK_send_events_mailings_MailingId",
                    column: x => x.MailingId,
                    principalTable: "mailings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_send_events_MailingId",
            table: "send_events",
            column: "MailingId");

        migrationBuilder.CreateIndex(
            name: "IX_send_events_MailingId_RecipientEmail",
            table: "send_events",
            columns: new[] { "MailingId", "RecipientEmail" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_send_events_MailingId_Status_CreatedAt",
            table: "send_events",
            columns: new[] { "MailingId", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_send_events_OwnerEmail_UpdatedAt",
            table: "send_events",
            columns: new[] { "OwnerEmail", "UpdatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "send_events");
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

[Migration("20260628090000_AddMailingDraftAttachments")]
public partial class AddMailingDraftAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AttachmentsJson",
            table: "mailing_message_drafts",
            type: "text",
            nullable: false,
            defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AttachmentsJson",
            table: "mailing_message_drafts");
    }
}

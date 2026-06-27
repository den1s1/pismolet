using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

[Migration("20260628093000_AddMailingDraftBodyFormat")]
public partial class AddMailingDraftBodyFormat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BodyFormat",
            table: "mailing_message_drafts",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: string.Empty);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BodyFormat",
            table: "mailing_message_drafts");
    }
}

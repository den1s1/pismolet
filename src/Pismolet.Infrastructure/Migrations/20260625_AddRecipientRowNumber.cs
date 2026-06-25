using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

[Migration("20260625_AddRecipientRowNumber")]
public partial class AddRecipientRowNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RowNumber",
            table: "recipients",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_recipients_MailingId_RowNumber",
            table: "recipients",
            columns: new[] { "MailingId", "RowNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_recipients_MailingId_RowNumber",
            table: "recipients");

        migrationBuilder.DropColumn(
            name: "RowNumber",
            table: "recipients");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

public partial class AddDeclarationImportBatchId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ImportBatchId",
            table: "mailing_declarations",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_mailing_declarations_ImportBatchId",
            table: "mailing_declarations",
            column: "ImportBatchId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_mailing_declarations_ImportBatchId",
            table: "mailing_declarations");

        migrationBuilder.DropColumn(
            name: "ImportBatchId",
            table: "mailing_declarations");
    }
}

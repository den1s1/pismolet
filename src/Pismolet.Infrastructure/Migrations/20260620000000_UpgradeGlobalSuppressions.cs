using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

public partial class UpgradeGlobalSuppressions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_global_suppressions",
            table: "global_suppressions");

        migrationBuilder.RenameColumn(
            name: "NormalizedEmail",
            table: "global_suppressions",
            newName: "EmailNormalized");

        migrationBuilder.AddColumn<Guid>(
            name: "Id",
            table: "global_suppressions",
            type: "uuid",
            nullable: false,
            defaultValueSql: "gen_random_uuid()");

        migrationBuilder.AddColumn<string>(
            name: "EmailHash",
            table: "global_suppressions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Source",
            table: "global_suppressions",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "Admin");

        migrationBuilder.AddColumn<Guid>(
            name: "SourceMailingId",
            table: "global_suppressions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SourceRecipientKey",
            table: "global_suppressions",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CreatedIpHash",
            table: "global_suppressions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UserAgentHash",
            table: "global_suppressions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.Sql("UPDATE global_suppressions SET \"EmailHash\" = encode(sha256(convert_to(\"EmailNormalized\", 'UTF8')), 'hex') WHERE \"EmailHash\" = '';");

        migrationBuilder.AddPrimaryKey(
            name: "PK_global_suppressions",
            table: "global_suppressions",
            column: "Id");

        migrationBuilder.CreateIndex(
            name: "IX_global_suppressions_EmailNormalized",
            table: "global_suppressions",
            column: "EmailNormalized",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_global_suppressions_EmailHash",
            table: "global_suppressions",
            column: "EmailHash");

        migrationBuilder.CreateIndex(
            name: "IX_global_suppressions_SourceMailingId",
            table: "global_suppressions",
            column: "SourceMailingId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_global_suppressions_SourceMailingId", table: "global_suppressions");
        migrationBuilder.DropIndex(name: "IX_global_suppressions_EmailHash", table: "global_suppressions");
        migrationBuilder.DropIndex(name: "IX_global_suppressions_EmailNormalized", table: "global_suppressions");
        migrationBuilder.DropPrimaryKey(name: "PK_global_suppressions", table: "global_suppressions");

        migrationBuilder.DropColumn(name: "Id", table: "global_suppressions");
        migrationBuilder.DropColumn(name: "EmailHash", table: "global_suppressions");
        migrationBuilder.DropColumn(name: "Source", table: "global_suppressions");
        migrationBuilder.DropColumn(name: "SourceMailingId", table: "global_suppressions");
        migrationBuilder.DropColumn(name: "SourceRecipientKey", table: "global_suppressions");
        migrationBuilder.DropColumn(name: "CreatedIpHash", table: "global_suppressions");
        migrationBuilder.DropColumn(name: "UserAgentHash", table: "global_suppressions");

        migrationBuilder.RenameColumn(
            name: "EmailNormalized",
            table: "global_suppressions",
            newName: "NormalizedEmail");

        migrationBuilder.AddPrimaryKey(
            name: "PK_global_suppressions",
            table: "global_suppressions",
            column: "NormalizedEmail");
    }
}

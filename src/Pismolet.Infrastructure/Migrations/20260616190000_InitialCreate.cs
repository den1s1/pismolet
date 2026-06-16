using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ConfirmationToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                ProfileStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                DailySendLimit = table.Column<int>(type: "integer", nullable: false),
                TotalSendLimit = table.Column<int>(type: "integer", nullable: false),
                PremoderationRequired = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Email);
            });

        migrationBuilder.CreateTable(
            name: "mailings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                StatusRu = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                PublicId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mailings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "audit_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                User = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Ip = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Context = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "global_suppressions",
            columns: table => new
            {
                NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_global_suppressions", x => x.NormalizedEmail);
            });

        migrationBuilder.CreateTable(
            name: "import_batches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                SourceFormat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TotalRows = table.Column<int>(type: "integer", nullable: false),
                Accepted = table.Column<int>(type: "integer", nullable: false),
                Duplicates = table.Column<int>(type: "integer", nullable: false),
                Invalid = table.Column<int>(type: "integer", nullable: false),
                GloballySuppressed = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_import_batches", x => x.Id);
                table.ForeignKey("FK_import_batches_mailings_MailingId", x => x.MailingId, "mailings", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "mailing_declarations",
            columns: table => new
            {
                MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                UserEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                BaseSource = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                IsBaseLegalityConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                IsAdvertisingConsentConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                DeclarationVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Ip = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mailing_declarations", x => x.MailingId);
                table.ForeignKey("FK_mailing_declarations_mailings_MailingId", x => x.MailingId, "mailings", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "mailing_message_drafts",
            columns: table => new
            {
                MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                SenderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Body = table.Column<string>(type: "text", nullable: false),
                MessageType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mailing_message_drafts", x => x.MailingId);
                table.ForeignKey("FK_mailing_message_drafts_mailings_MailingId", x => x.MailingId, "mailings", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "import_issues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                RowNumber = table.Column<int>(type: "integer", nullable: false),
                Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_import_issues", x => x.Id);
                table.ForeignKey("FK_import_issues_import_batches_ImportBatchId", x => x.ImportBatchId, "import_batches", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "recipients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ExclusionReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ImportBatchId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_recipients", x => x.Id);
                table.ForeignKey("FK_recipients_import_batches_ImportBatchId", x => x.ImportBatchId, "import_batches", "Id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("FK_recipients_mailings_MailingId", x => x.MailingId, "mailings", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_users_NormalizedEmail", "users", "NormalizedEmail", unique: true);
        migrationBuilder.CreateIndex("IX_mailings_OwnerEmail", "mailings", "OwnerEmail");
        migrationBuilder.CreateIndex("IX_audit_records_User", "audit_records", "User");
        migrationBuilder.CreateIndex("IX_import_batches_MailingId", "import_batches", "MailingId");
        migrationBuilder.CreateIndex("IX_import_issues_ImportBatchId", "import_issues", "ImportBatchId");
        migrationBuilder.CreateIndex("IX_recipients_ImportBatchId", "recipients", "ImportBatchId");
        migrationBuilder.CreateIndex("IX_recipients_MailingId", "recipients", "MailingId");
        migrationBuilder.CreateIndex("IX_recipients_NormalizedEmail", "recipients", "NormalizedEmail");
        migrationBuilder.CreateIndex("IX_mailing_declarations_UserEmail", "mailing_declarations", "UserEmail");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("audit_records");
        migrationBuilder.DropTable("global_suppressions");
        migrationBuilder.DropTable("import_issues");
        migrationBuilder.DropTable("mailing_declarations");
        migrationBuilder.DropTable("mailing_message_drafts");
        migrationBuilder.DropTable("recipients");
        migrationBuilder.DropTable("users");
        migrationBuilder.DropTable("import_batches");
        migrationBuilder.DropTable("mailings");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Infrastructure.Migrations.Postmaster
{
    /// <inheritdoc />
    public partial class AddMailruPostmasterMailingMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mailru_postmaster_mailing_daily_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                    MsgType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MessagesSent = table.Column<long>(type: "bigint", nullable: false),
                    Delivered = table.Column<long>(type: "bigint", nullable: false),
                    ProbablySpam = table.Column<long>(type: "bigint", nullable: false),
                    Spam = table.Column<long>(type: "bigint", nullable: false),
                    Complaints = table.Column<long>(type: "bigint", nullable: false),
                    Read = table.Column<long>(type: "bigint", nullable: false),
                    DeletedRead = table.Column<long>(type: "bigint", nullable: false),
                    DeletedUnread = table.Column<long>(type: "bigint", nullable: false),
                    SpamPercent = table.Column<double>(type: "double precision", nullable: false),
                    ProbablySpamPercent = table.Column<double>(type: "double precision", nullable: false),
                    Reputation = table.Column<double>(type: "double precision", nullable: false),
                    Trend = table.Column<double>(type: "double precision", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailru_postmaster_mailing_daily_metrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mailru_postmaster_mailing_sync_states",
                columns: table => new
                {
                    MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    MsgType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstSendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastDateTo = table.Column<DateOnly>(type: "date", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDataFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UnchangedSuccessCount = table.Column<int>(type: "integer", nullable: false),
                    HasData = table.Column<bool>(type: "boolean", nullable: false),
                    IsStable = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailru_postmaster_mailing_sync_states", x => new { x.Domain, x.MailingId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_mailing_daily_metrics_Domain_MailingId_Da~",
                table: "mailru_postmaster_mailing_daily_metrics",
                columns: new[] { "Domain", "MailingId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_mailing_daily_metrics_MailingId_Date",
                table: "mailru_postmaster_mailing_daily_metrics",
                columns: new[] { "MailingId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_mailing_daily_metrics_MsgType",
                table: "mailru_postmaster_mailing_daily_metrics",
                column: "MsgType");

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_mailing_sync_states_MsgType",
                table: "mailru_postmaster_mailing_sync_states",
                column: "MsgType");

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_mailing_sync_states_NextAttemptAt_Complet~",
                table: "mailru_postmaster_mailing_sync_states",
                columns: new[] { "NextAttemptAt", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailru_postmaster_mailing_daily_metrics");

            migrationBuilder.DropTable(
                name: "mailru_postmaster_mailing_sync_states");
        }
    }
}

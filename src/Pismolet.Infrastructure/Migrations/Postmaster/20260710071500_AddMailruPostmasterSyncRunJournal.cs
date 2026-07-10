using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Infrastructure.Migrations.Postmaster
{
    /// <inheritdoc />
    public partial class AddMailruPostmasterSyncRunJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mailru_postmaster_sync_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DateFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    DateTo = table.Column<DateOnly>(type: "date", nullable: true),
                    MetricDays = table.Column<int>(type: "integer", nullable: false),
                    TroubleCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailru_postmaster_sync_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_sync_runs_Domain_StartedAt",
                table: "mailru_postmaster_sync_runs",
                columns: new[] { "Domain", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_sync_runs_Domain_Trigger_StartedAt",
                table: "mailru_postmaster_sync_runs",
                columns: new[] { "Domain", "Trigger", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailru_postmaster_sync_runs");
        }
    }
}

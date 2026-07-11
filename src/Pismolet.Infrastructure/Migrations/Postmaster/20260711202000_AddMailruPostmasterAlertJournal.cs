using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Infrastructure.Migrations.Postmaster
{
    /// <inheritdoc />
    public partial class AddMailruPostmasterAlertJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mailru_postmaster_alert_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Severity = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DateFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    DateTo = table.Column<DateOnly>(type: "date", nullable: true),
                    MessagesSent = table.Column<long>(type: "bigint", nullable: false),
                    ObservedValue = table.Column<double>(type: "double precision", nullable: false),
                    ThresholdValue = table.Column<double>(type: "double precision", nullable: false),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastNotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailru_postmaster_alert_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_alert_events_Domain_Code_Fingerprint",
                table: "mailru_postmaster_alert_events",
                columns: new[] { "Domain", "Code", "Fingerprint" },
                unique: true,
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_alert_events_Domain_Severity_UpdatedAt",
                table: "mailru_postmaster_alert_events",
                columns: new[] { "Domain", "Severity", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_mailru_postmaster_alert_events_Domain_Status_UpdatedAt",
                table: "mailru_postmaster_alert_events",
                columns: new[] { "Domain", "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailru_postmaster_alert_events");
        }
    }
}

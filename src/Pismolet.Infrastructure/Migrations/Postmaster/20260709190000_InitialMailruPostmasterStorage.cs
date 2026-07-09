using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pismolet.Web.Infrastructure.Postmaster;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations.Postmaster;

[DbContext(typeof(MailruPostmasterDbContext))]
[Migration("20260709190000_InitialMailruPostmasterStorage")]
public partial class InitialMailruPostmasterStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "mailru_postmaster_domain_daily_metrics",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_mailru_postmaster_domain_daily_metrics", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "mailru_postmaster_sync_states",
            columns: table => new
            {
                Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastErrorCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                LastErrorSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                LastDomainDate = table.Column<DateOnly>(type: "date", nullable: true),
                LastMailingSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mailru_postmaster_sync_states", x => x.Domain);
            });

        migrationBuilder.CreateTable(
            name: "mailru_postmaster_trouble_snapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                Code = table.Column<int>(type: "integer", nullable: false),
                Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mailru_postmaster_trouble_snapshots", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_mailru_postmaster_domain_daily_metrics_Date",
            table: "mailru_postmaster_domain_daily_metrics",
            column: "Date");

        migrationBuilder.CreateIndex(
            name: "IX_mailru_postmaster_domain_daily_metrics_Domain_Date",
            table: "mailru_postmaster_domain_daily_metrics",
            columns: new[] { "Domain", "Date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_mailru_postmaster_trouble_snapshots_Domain_Code",
            table: "mailru_postmaster_trouble_snapshots",
            columns: new[] { "Domain", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_mailru_postmaster_trouble_snapshots_Domain_IsActive",
            table: "mailru_postmaster_trouble_snapshots",
            columns: new[] { "Domain", "IsActive" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "mailru_postmaster_domain_daily_metrics");
        migrationBuilder.DropTable(name: "mailru_postmaster_sync_states");
        migrationBuilder.DropTable(name: "mailru_postmaster_trouble_snapshots");
    }
}

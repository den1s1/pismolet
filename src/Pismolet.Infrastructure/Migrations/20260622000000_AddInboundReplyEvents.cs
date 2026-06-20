using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

public partial class AddInboundReplyEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS reply_events (
    ""Id"" uuid PRIMARY KEY,
    ""Provider"" character varying(80) NOT NULL,
    ""ProviderInboundEventId"" character varying(240) NOT NULL,
    ""MailingId"" uuid NULL,
    ""ClientId"" character varying(254) NULL,
    ""RecipientEmailNormalized"" character varying(254) NULL,
    ""FromEmailNormalized"" character varying(254) NOT NULL,
    ""ToAddress"" character varying(512) NOT NULL,
    ""ReplyTokenHash"" character varying(64) NULL,
    ""SubjectPreview"" character varying(200) NOT NULL,
    ""ReceivedAt"" timestamp with time zone NOT NULL,
    ""ProcessedAt"" timestamp with time zone NULL,
    ""ForwardQueuedAt"" timestamp with time zone NULL,
    ""ForwardedAt"" timestamp with time zone NULL,
    ""ForwardToEmailNormalized"" character varying(254) NULL,
    ""ProcessingStatus"" character varying(40) NOT NULL,
    ""ForwardRetryCount"" integer NOT NULL,
    ""BodyStorageStatus"" character varying(40) NOT NULL,
    ""BodyExpiresAt"" timestamp with time zone NULL,
    ""BodyTextStored"" character varying(16000) NULL,
    ""RawPayloadHash"" character varying(64) NOT NULL,
    ""ErrorCode"" character varying(120) NULL,
    ""ErrorMessage"" character varying(1000) NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_reply_events_Provider_ProviderInboundEventId"" ON reply_events (""Provider"", ""ProviderInboundEventId"");
CREATE INDEX IF NOT EXISTS ""IX_reply_events_MailingId_ReceivedAt"" ON reply_events (""MailingId"", ""ReceivedAt"");
CREATE INDEX IF NOT EXISTS ""IX_reply_events_ClientId_ReceivedAt"" ON reply_events (""ClientId"", ""ReceivedAt"");
CREATE INDEX IF NOT EXISTS ""IX_reply_events_ProcessingStatus_ReceivedAt"" ON reply_events (""ProcessingStatus"", ""ReceivedAt"");
CREATE INDEX IF NOT EXISTS ""IX_reply_events_BodyExpiresAt"" ON reply_events (""BodyExpiresAt"");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS reply_events;");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

public partial class AddProviderWebhooksAndClientSuppressions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ClientSuppressed",
            table: "import_batches",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "DeliveryStatus",
            table: "send_events",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "NotReported");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastDeliveryEventAt",
            table: "send_events",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastDeliverySummary",
            table: "send_events",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "provider_webhook_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ProviderEventId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                ProviderMessageId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                MailingId = table.Column<Guid>(type: "uuid", nullable: true),
                ClientId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                RecipientEmailNormalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RawPayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RawPayloadStored = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                ReasonCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                ReasonMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ProcessingStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provider_webhook_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "client_suppressions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                EmailNormalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                SourceMailingId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceProviderMessageId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_client_suppressions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_send_events_ProviderMessageId",
            table: "send_events",
            column: "ProviderMessageId");

        migrationBuilder.CreateIndex(
            name: "IX_send_events_MailingId_DeliveryStatus",
            table: "send_events",
            columns: new[] { "MailingId", "DeliveryStatus" });

        migrationBuilder.CreateIndex(
            name: "IX_provider_webhook_events_Provider_ProviderEventId",
            table: "provider_webhook_events",
            columns: new[] { "Provider", "ProviderEventId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_provider_webhook_events_ProviderMessageId",
            table: "provider_webhook_events",
            column: "ProviderMessageId");

        migrationBuilder.CreateIndex(
            name: "IX_provider_webhook_events_MailingId",
            table: "provider_webhook_events",
            column: "MailingId");

        migrationBuilder.CreateIndex(
            name: "IX_provider_webhook_events_MailingId_EventType",
            table: "provider_webhook_events",
            columns: new[] { "MailingId", "EventType" });

        migrationBuilder.CreateIndex(
            name: "IX_client_suppressions_ClientId_EmailNormalized",
            table: "client_suppressions",
            columns: new[] { "ClientId", "EmailNormalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_client_suppressions_EmailNormalized",
            table: "client_suppressions",
            column: "EmailNormalized");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "provider_webhook_events");
        migrationBuilder.DropTable(name: "client_suppressions");

        migrationBuilder.DropIndex(name: "IX_send_events_ProviderMessageId", table: "send_events");
        migrationBuilder.DropIndex(name: "IX_send_events_MailingId_DeliveryStatus", table: "send_events");

        migrationBuilder.DropColumn(name: "ClientSuppressed", table: "import_batches");
        migrationBuilder.DropColumn(name: "DeliveryStatus", table: "send_events");
        migrationBuilder.DropColumn(name: "LastDeliveryEventAt", table: "send_events");
        migrationBuilder.DropColumn(name: "LastDeliverySummary", table: "send_events");
    }
}
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations;

[Migration("20260628123000_AddReplyAliasMessageMapping")]
public partial class AddReplyAliasMessageMapping : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "client_reply_aliases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Alias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_client_reply_aliases", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbound_reply_message_mappings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MessageId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                MailingId = table.Column<Guid>(type: "uuid", nullable: false),
                SendEventId = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                RecipientEmailNormalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                ReplyAlias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbound_reply_message_mappings", x => x.Id);
                table.ForeignKey(
                    name: "FK_outbound_reply_message_mappings_mailings_MailingId",
                    column: x => x.MailingId,
                    principalTable: "mailings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_outbound_reply_message_mappings_send_events_SendEventId",
                    column: x => x.SendEventId,
                    principalTable: "send_events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_client_reply_aliases_Alias",
            table: "client_reply_aliases",
            column: "Alias",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_client_reply_aliases_ClientId",
            table: "client_reply_aliases",
            column: "ClientId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbound_reply_message_mappings_ClientId_CreatedAt",
            table: "outbound_reply_message_mappings",
            columns: new[] { "ClientId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_outbound_reply_message_mappings_MailingId",
            table: "outbound_reply_message_mappings",
            column: "MailingId");

        migrationBuilder.CreateIndex(
            name: "IX_outbound_reply_message_mappings_MessageId",
            table: "outbound_reply_message_mappings",
            column: "MessageId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbound_reply_message_mappings_ReplyAlias_CreatedAt",
            table: "outbound_reply_message_mappings",
            columns: new[] { "ReplyAlias", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_outbound_reply_message_mappings_SendEventId",
            table: "outbound_reply_message_mappings",
            column: "SendEventId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbound_reply_message_mappings");
        migrationBuilder.DropTable(name: "client_reply_aliases");
    }
}

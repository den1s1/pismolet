-- Sprint 5.5.1 - click tracking backend
-- Safe to run more than once.

CREATE TABLE IF NOT EXISTS tracked_links (
    "Id" uuid PRIMARY KEY,
    "MailingId" uuid NOT NULL REFERENCES mailings("Id") ON DELETE CASCADE,
    "RecipientEmail" character varying(254) NOT NULL,
    "Token" character varying(64) NOT NULL,
    "OriginalUrl" character varying(2048) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "FirstClickedAt" timestamp with time zone NULL,
    "LastClickedAt" timestamp with time zone NULL,
    "ClickCount" integer NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS click_events (
    "Id" uuid PRIMARY KEY,
    "TrackedLinkId" uuid NOT NULL REFERENCES tracked_links("Id") ON DELETE CASCADE,
    "MailingId" uuid NOT NULL REFERENCES mailings("Id") ON DELETE CASCADE,
    "RecipientEmail" character varying(254) NOT NULL,
    "Token" character varying(64) NOT NULL,
    "OriginalUrl" character varying(2048) NOT NULL,
    "ClickedAt" timestamp with time zone NOT NULL,
    "IpHash" character varying(64) NULL,
    "UserAgentHash" character varying(64) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_tracked_links_Token"
    ON tracked_links ("Token");

CREATE INDEX IF NOT EXISTS "IX_tracked_links_MailingId"
    ON tracked_links ("MailingId");

CREATE INDEX IF NOT EXISTS "IX_tracked_links_MailingId_RecipientEmail"
    ON tracked_links ("MailingId", "RecipientEmail");

CREATE INDEX IF NOT EXISTS "IX_click_events_TrackedLinkId"
    ON click_events ("TrackedLinkId");

CREATE INDEX IF NOT EXISTS "IX_click_events_MailingId_ClickedAt"
    ON click_events ("MailingId", "ClickedAt");

CREATE INDEX IF NOT EXISTS "IX_click_events_MailingId_RecipientEmail"
    ON click_events ("MailingId", "RecipientEmail");

CREATE TABLE IF NOT EXISTS postfix_delivery_events (
    "Id" uuid PRIMARY KEY,
    "QueueId" varchar(64) NOT NULL,
    "RecipientEmail" varchar(254) NOT NULL,
    "Status" varchar(40) NOT NULL,
    "DeliveryStatus" varchar(40) NOT NULL,
    "Dsn" varchar(40) NULL,
    "Relay" varchar(512) NULL,
    "Diagnostic" varchar(2000) NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_postfix_delivery_events_QueueId_RecipientEmail_Status_OccurredAt"
    ON postfix_delivery_events ("QueueId", "RecipientEmail", "Status", "OccurredAt");

CREATE INDEX IF NOT EXISTS "IX_postfix_delivery_events_OccurredAt"
    ON postfix_delivery_events ("OccurredAt");

CREATE INDEX IF NOT EXISTS "IX_postfix_delivery_events_RecipientEmail_OccurredAt"
    ON postfix_delivery_events ("RecipientEmail", "OccurredAt");

CREATE INDEX IF NOT EXISTS "IX_postfix_delivery_events_DeliveryStatus_OccurredAt"
    ON postfix_delivery_events ("DeliveryStatus", "OccurredAt");

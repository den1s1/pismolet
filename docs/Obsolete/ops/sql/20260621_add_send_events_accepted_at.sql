-- Adds immutable accepted-send timestamp used by warmup and daily send limits.
-- Safe to run repeatedly on PostgreSQL.

ALTER TABLE send_events
    ADD COLUMN IF NOT EXISTS "AcceptedAt" timestamp with time zone;

UPDATE send_events
SET "AcceptedAt" = "UpdatedAt"
WHERE "Status" = 'Accepted'
  AND "AcceptedAt" IS NULL;

CREATE INDEX IF NOT EXISTS "IX_send_events_owner_email_accepted_at"
    ON send_events ("OwnerEmail", "AcceptedAt");

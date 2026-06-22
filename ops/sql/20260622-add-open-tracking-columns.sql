BEGIN;

ALTER TABLE send_events
    ADD COLUMN IF NOT EXISTS "TrackingToken" text,
    ADD COLUMN IF NOT EXISTS "FirstOpenedAt" timestamp with time zone,
    ADD COLUMN IF NOT EXISTS "LastOpenedAt" timestamp with time zone,
    ADD COLUMN IF NOT EXISTS "OpenCount" integer NOT NULL DEFAULT 0;

ALTER TABLE send_events
    ALTER COLUMN "TrackingToken" TYPE text USING "TrackingToken"::text;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_send_events_TrackingToken"
    ON send_events ("TrackingToken")
    WHERE "TrackingToken" IS NOT NULL;

COMMIT;

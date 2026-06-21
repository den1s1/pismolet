BEGIN;

DO $$
BEGIN
    IF to_regclass('public.mailings') IS NULL THEN
        RAISE EXCEPTION 'Cannot baseline EF history: table mailings is missing.';
    END IF;

    IF to_regclass('public.recipients') IS NULL THEN
        RAISE EXCEPTION 'Cannot baseline EF history: table recipients is missing.';
    END IF;

    IF to_regclass('public.send_events') IS NULL THEN
        RAISE EXCEPTION 'Cannot baseline EF history: table send_events is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'send_events'
          AND column_name = 'AcceptedAt'
    ) THEN
        RAISE EXCEPTION 'Cannot baseline EF history: send_events.AcceptedAt is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'send_events'
          AND column_name = 'AcceptedUtcDay'
    ) THEN
        RAISE EXCEPTION 'Cannot baseline EF history: send_events.AcceptedUtcDay is missing.';
    END IF;

    IF to_regclass('public.provider_webhook_events') IS NULL THEN
        RAISE EXCEPTION 'Cannot baseline EF history: table provider_webhook_events is missing.';
    END IF;

    IF to_regclass('public.client_suppressions') IS NULL THEN
        RAISE EXCEPTION 'Cannot baseline EF history: table client_suppressions is missing.';
    END IF;

    IF to_regclass('public.reply_events') IS NULL THEN
        RAISE EXCEPTION 'Cannot baseline EF history: table reply_events is missing.';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260616190000_InitialCreate', '9.0.17'),
    ('20260616203000_AddDeclarationImportBatchId', '9.0.17'),
    ('20260618000000_AddSendEvents', '9.0.17'),
    ('20260620000000_UpgradeGlobalSuppressions', '9.0.17'),
    ('20260621000000_AddProviderWebhooksAndClientSuppressions', '9.0.17'),
    ('20260621191000_AddSendEventsAcceptedAt', '9.0.17'),
    ('20260621194000_AddSendEventsAcceptedUtcDay', '9.0.17'),
    ('20260622000000_AddInboundReplyEvents', '9.0.17')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

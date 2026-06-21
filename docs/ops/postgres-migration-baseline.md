# Production EF migration baseline

This note describes how to register the current production PostgreSQL schema as an EF Core migration baseline.

Use this only after a database backup and after checking that production schema already contains the objects created by the existing migration files.

## Why this exists

The production database was created before EF migration history was present. The application schema is already in place, including the recent `send_events` columns `AcceptedAt` and `AcceptedUtcDay`, but the table `__EFMigrationsHistory` is absent.

Without a baseline, future EF migration tooling cannot reliably distinguish already applied schema changes from new ones.

## Safety rules

1. Make a dump first.
2. Run the verification queries from this document.
3. Apply `ops/sql/20260621-baseline-ef-migrations-history.sql` only if the checks pass.
4. Do not use this script for an empty database.
5. Do not use this script to repair missing business tables or columns. Missing schema must be fixed explicitly before baseline registration.

## Backup

```bash
cd /opt/pismolet

CONN="$(sudo grep '^ConnectionStrings__PismoletDb=' /etc/pismolet/pismolet.env | cut -d= -f2-)"
PGHOST="$(printf '%s' "$CONN" | tr ';' '\n' | awk -F= '$1=="Host"{print $2}')"
PGPORT="$(printf '%s' "$CONN" | tr ';' '\n' | awk -F= '$1=="Port"{print $2}')"
PGDATABASE="$(printf '%s' "$CONN" | tr ';' '\n' | awk -F= '$1=="Database"{print $2}')"
PGUSER="$(printf '%s' "$CONN" | tr ';' '\n' | awk -F= '$1=="Username"{print $2}')"
PGPASSWORD="$(printf '%s' "$CONN" | tr ';' '\n' | awk -F= '$1=="Password"{print $2}')"
export PGPASSWORD

BACKUP="/root/pismolet-db-before-ef-history-baseline-$(date +%Y%m%d-%H%M%S).dump"
pg_dump -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -Fc -f "$BACKUP"
ls -lh "$BACKUP"
```

## Verification

```bash
psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -P pager=off -c "
SELECT to_regclass('public.""__EFMigrationsHistory""') AS history_table;
"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -P pager=off -c "
SELECT table_name
FROM information_schema.tables
WHERE table_schema='public'
ORDER BY table_name;
"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -P pager=off -c "
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE table_schema='public'
  AND (
    table_name IN ('send_events', 'reply_events', 'provider_webhook_events', 'client_suppressions', 'global_suppressions')
    OR column_name IN ('AcceptedAt', 'AcceptedUtcDay', 'ImportBatchId')
  )
ORDER BY table_name, ordinal_position;
"
```

Expected before applying the baseline:

- `history_table` is empty.
- Existing business tables are present.
- `send_events` has `AcceptedAt` and `AcceptedUtcDay`.
- Inbound replies and webhook/suppression tables are present if the corresponding migrations are listed in the baseline script.

## Apply baseline

```bash
psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -v ON_ERROR_STOP=1 -f ops/sql/20260621-baseline-ef-migrations-history.sql
```

## Check result

```bash
psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -P pager=off -c '
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";
'
```

After this baseline, new schema changes must be added as normal EF migrations and applied in order.

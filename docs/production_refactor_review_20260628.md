# Production refactor review, 2026-06-28

Source revision: `origin/Development` at `ba8902d0524099bed718a4e5e1de137963466edf`.

## Baseline

Status: done.

- `dotnet build Pismolet.sln /nr:false -m:1` passed.
- `dotnet test Pismolet.sln --no-build` passed: Integration 4/4, Web 302/302.

## Review Findings

### P0. Attachments are not persisted by EF

Status: fixed in Sprint 1.

Attachments are accepted by the UI/domain/application layer and sent by `SmtpEmailProviderAdapter`, but `MailingMessageDraftEntity` has no attachment storage. `EfMailingRepository` saves and reads only sender, subject, body, message type and updated date. In Postgres mode, attachments disappear after the mailing is saved and reloaded.

Needed tests:

- EF roundtrip saves attachment metadata and bytes.
- Editing a message without uploading new attachments keeps existing saved attachments.

### P1. Message body format is inferred from text instead of persisted

Status: planned.

The editor has explicit text/html tabs, but the saved domain model stores only `Body`. Rendering and preview infer HTML by tag heuristics. A plain-text message containing HTML-like fragments can be sent as HTML, and some HTML fragments can be misclassified.

Needed refactor:

- Add explicit message body format to the domain model and EF.
- Keep backward compatibility by inferring format for old rows during migration/read.
- Cover text with `<p`-like content and HTML fragment without full document wrappers.

### P1. Automatic sender needs stronger queue idempotency coverage

Status: planned.

`ExecuteQueuedBatchAsync` re-enqueues the mailing while pending events remain and relies on `SendEvent` state for idempotency. Existing tests cover warmup and basic launch, but not overlapping jobs/reruns against the same mailing under realistic repository behavior.

Needed tests:

- Two sequential worker executions do not send accepted recipients twice.
- Re-running a job after partial provider failure resumes only pending events.
- Warmup delayed job does not create an immediate hot loop.

### P1. Reply forwarding needs a real SMTP/MIME smoke before production claim

Status: planned.

Inbound parsing and processing are covered, but practical forwarding is still not verified with a real SMTP path. The current code forwards stored body text only and intentionally avoids raw payload. That is safe, but needs a smoke test with real-like MIME and delivery response.

Needed tests:

- Forwarded reply includes client recipient, source sender, subject preview and body status.
- Auto-reply ignored replies are not queued for forward.
- Failed forward can be retried without creating a duplicate `ReplyEvent`.

### P2. Admin warmup settings are runtime-file backed and require restart

Status: planned.

Admin UI saves warmup limits to a JSON file. Current process reads runtime settings through repository reads, but some option objects may still be built at startup. Need verify all sending paths read latest settings when evaluating warmup.

Needed tests:

- Saving admin warmup settings changes subsequent `MailWarmupSendGate` decisions without stale config.
- Invalid setting combinations keep previous effective settings.

### P2. User deletion needs broader cascade/privacy tests

Status: planned.

User removal code exists and has endpoint tests, but production-risk entities include mailings, payments, audit, replies, suppressions and auth artifacts. Need document what is deleted vs retained/anonymized and cover repository behavior.

Needed tests:

- Deleting a user removes/blocks login credentials.
- Owned mailings and payment data follow the intended retention policy.
- Admin cannot delete self/root admin accidentally.

## Refactor Plan

### Sprint 1. Persist attachments in EF

Status: completed.

Tasks:

- Add EF storage for `MailingMessageDraft.Attachments`.
- Add migration and model snapshot update.
- Add EF roundtrip tests.
- Run targeted tests, build and full test gate.

Result:

- Added `AttachmentsJson` to `mailing_message_drafts`.
- `EfMailingRepository` now serializes/deserializes attachment metadata and bytes.
- Fixed SQLite-unsafe `DateTimeOffset` ordering in EF mailing batch materialization.
- Added EF roundtrip and application-service preservation tests.
- Gate passed: `EfMailingRepositoryTests` 2/2, build, full tests Integration 4/4 and Web 304/304.

### Sprint 2. Persist explicit message body format

Status: pending.

Tasks:

- Add `MessageBodyFormat` value to domain and EF.
- Stop using HTML tag sniffing as the primary source of truth.
- Keep old data readable.
- Add editor, preview and SMTP rendering tests.

### Sprint 3. Harden automatic sender idempotency

Status: pending.

Tasks:

- Extract pending-batch transition rules into a small testable component if needed.
- Add duplicate/rerun/partial-failure tests.
- Verify Hangfire delayed warmup path does not hot-loop.

### Sprint 4. Reply forwarding practical coverage

Status: pending.

Tasks:

- Add SMTP adapter tests for reply forward MIME shape.
- Add retry/no-duplicate tests around `ExecuteForwardAsync`.
- Update `docs/inbound_reply_sprint_status.md`.

### Sprint 5. Admin warmup settings consistency

Status: pending.

Tasks:

- Trace config reads from admin save to warmup gate.
- Add tests for updated runtime settings and invalid saves.
- Simplify stale option paths if found.

### Sprint 6. User deletion retention policy

Status: pending.

Tasks:

- Review deletion service coverage across user-owned data.
- Add repository/service tests for intended deletion/anonymization.
- Document residual retained data.

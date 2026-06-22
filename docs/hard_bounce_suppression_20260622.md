# Hard bounce auto suppression

Date: 2026-06-22

Status: implemented, pending production verification.

Purpose:

- protect sender reputation;
- stop resending to recipients that produced a permanent delivery failure;
- keep temporary failures separate from permanent failures.

Implemented behavior:

- Postfix `bounced` and `expired` delivery events map to `DeliveryStatus = HardBounce`.
- When such an event is matched to a `send_events` row, the recipient is added to client suppression list.
- Suppression is scoped by `sendEvent.OwnerEmail` as client id.
- Suppression source stores:
  - `SourceMailingId`;
  - `SourceProviderMessageId`, which is the Postfix queue id.
- Existing suppression rows are touched instead of duplicated.
- `SoftBounce` does not create suppression.
- `Delivered` does not create suppression.

Counters:

- `PostfixDeliveryLogIngestionResult.ClientSuppressions` shows how many client suppression rows were added or touched during a log ingestion run.

Tests:

- hard bounce creates client suppression;
- soft bounce does not create client suppression;
- delivery status application still updates matching send events.

Production verification plan:

1. Pull and deploy.
2. Trigger or simulate a permanent bounce.
3. Confirm `send_events.DeliveryStatus = HardBounce`.
4. Confirm `client_suppressions` contains the recipient under the mailing owner.
5. Confirm future sends to that recipient are skipped as `ClientSuppression`.

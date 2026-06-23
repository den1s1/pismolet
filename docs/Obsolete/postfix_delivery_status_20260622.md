# Postfix delivery status verification

Date: 2026-06-22

Status: verified in production.

Confirmed flow:

- SMTP adapter stores Postfix queue id in `send_events.ProviderMessageId`.
- Postfix writes delivery result to `/var/log/mail.log`.
- Manual reader can read new log lines from cursor position.
- Background reader can read new log lines automatically.
- Parsed Postfix delivery events are stored in `postfix_delivery_events`.
- Matching `send_events` rows are updated with real delivery status.

Production confirmation:

- Queue id `F2B33842DB` was updated to `DeliveryStatus = Delivered` automatically.
- Queue id `CD71284177` was updated to `DeliveryStatus = Delivered` through the reader flow.
- Queue id `6853E83ED5` was updated to `DeliveryStatus = SoftBounce` because the recipient mailbox was over quota.

Current default:

- Automatic reader interval: 60 seconds.
- Settings page: `/admin/delivery/postfix/settings`.
- Manual reader page: `/admin/delivery/postfix`.

Notes:

- `Delivered` means the receiving SMTP server accepted the message. It does not mean the recipient opened or read it.
- `SoftBounce` is temporary. Postfix can retry and later deliver the message.
- Older messages can remain `NotReported` if the reader cursor was initialized after those log lines.

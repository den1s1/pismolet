# Delivery status rollout plan

Документ фиксирует план внедрения реальных статусов доставки на основе Postfix logs.

## Статус на 22 июня 2026 г.

- Sprint 5.6.1 - на проверке: добавлен изолированный parser строк `postfix/smtp`, без БД и без влияния на отправку писем.

## Почему это нужно

Сейчас статус `Accepted` означает, что приложение передало письмо локальному SMTP. Это важный технический статус, но он не равен фактической доставке до внешнего почтового сервера получателя.

Postfix logs позволяют увидеть следующий слой доставки:

- `sent` - Postfix передал письмо внешнему серверу;
- `deferred` - временная проблема доставки;
- `bounced` - письмо отклонено или не доставлено;
- `expired` - доставка не удалась после повторных попыток;
- `dsn` - диагностический код доставки;
- `relay` - внешний сервер, с которым работал Postfix;
- diagnostic text - текстовая причина от Postfix или внешнего SMTP.

## Sprint 5.6.1 - Postfix log parser

Статус: **на проверке**.

Цель: безопасно научиться разбирать строки `postfix/smtp`, не меняя отправку и не записывая данные в БД.

Входит:

- parser строк `postfix/smtp`;
- извлечение:
  - queue id;
  - recipient email;
  - status;
  - mapped delivery status;
  - dsn;
  - relay;
  - diagnostic text;
  - occurred at;
- тесты для `sent`, `deferred`, `bounced`;
- игнор нерелевантных строк, например `postfix/qmgr`.

Результат Sprint 5.6.1: есть безопасный parser, который можно проверить на реальных строках `/var/log/mail.log`, но он ещё ничего не меняет в production-данных.

## Sprint 5.6.2 - Delivery event storage

Статус: **запланирован**.

Цель: сохранить разобранные Postfix events в отдельной таблице и связать их с `send_events`.

План:

- добавить таблицу `postfix_delivery_events`;
- хранить queue id, recipient email, status, dsn, relay, diagnostic text, occurred at, raw line hash;
- защититься от дублей по hash или комбинации queue id + recipient + status + timestamp;
- добавить репозиторий и тесты;
- не обновлять `send_events` до отдельной проверки.

## Sprint 5.6.3 - Apply delivery status

Статус: **запланирован**.

Цель: обновлять существующие поля `send_events.DeliveryStatus`, `LastDeliveryEventAt`, `LastDeliverySummary` на основе Postfix events.

План:

- сопоставлять Postfix событие с `send_events`;
- сначала искать по `ProviderMessageId`/queue id, затем осторожно по recipient и временному окну;
- `sent` маппить в `Delivered`;
- `deferred` маппить в `SoftBounce`;
- `bounced` и `expired` маппить в `HardBounce`;
- применять уже существующую логику приоритета `ApplyDeliveryStatus`.

## Sprint 5.6.4 - UI delivery diagnostics

Статус: **запланирован**.

Цель: показать пользователю и администратору реальные статусы доставки и причины отказов.

План:

- добавить в ЛК строки: `Доставлено`, `Временные ошибки`, `Недоставлено`;
- в таблице получателей показывать статус доставки и диагностический текст;
- в админке добавить блок Postfix diagnostics;
- не показывать пользователю сырые технические данные без нормальной русской формулировки.

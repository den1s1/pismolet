# Delivery status rollout plan

Документ фиксирует план внедрения реальных статусов доставки на основе Postfix logs.

## Статус на 22 июня 2026 г.

- Sprint 5.6.1 - закрыт: добавлен изолированный parser строк `postfix/smtp`, без БД и без влияния на отправку писем.
- Sprint 5.6.2 - закрыт: добавлены доменная модель, SQL-скрипт, EF/InMemory репозитории и таблица `postfix_delivery_events`; `send_events` пока не обновляются.
- Sprint 5.6.3 - на проверке: добавлен сервис ingestion, который принимает строки Postfix-лога, парсит их и сохраняет новые события в `postfix_delivery_events`.

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

Статус: **закрыт**.

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

Статус: **закрыт**.

Цель: сохранить разобранные Postfix events в отдельной таблице. На этом шаге `send_events` ещё не обновляются.

Входит:

- таблица `postfix_delivery_events`;
- поля: queue id, recipient email, status, delivery status, dsn, relay, diagnostic text, occurred at, created at;
- защита от точных дублей по `queue id + recipient + status + occurred at`;
- EF-репозиторий;
- InMemory-репозиторий;
- DI-регистрация репозиториев;
- тесты на idempotency и хранение нескольких статусов для одного `queue id + recipient`.

Результат Sprint 5.6.2: приложение готово сохранять разобранные Postfix events, но ещё не применяет их к `send_events`.

## Sprint 5.6.3 - Delivery log ingestion

Статус: **на проверке**.

Цель: добавить сервис, который принимает строки Postfix-лога, парсит их и сохраняет новые delivery events.

Входит:

- `PostfixDeliveryLogIngestionService`;
- обработка строки или пачки строк;
- счётчики `parsed`, `stored`, `ignored`;
- сохранение только новых точных событий;
- сохранение нескольких разных статусов для одного `queue id + recipient`, например `deferred`, затем `sent`;
- тесты на сохранение, игнор нерелевантных строк, idempotency и несколько статусов.

Результат Sprint 5.6.3: приложение умеет принимать подготовленный набор строк Postfix-лога и сохранять их в БД. Автоматический сбор из `/var/log/mail.log` будет отдельным шагом.

## Sprint 5.6.4 - Apply delivery status

Статус: **запланирован**.

Цель: обновлять существующие поля `send_events.DeliveryStatus`, `LastDeliveryEventAt`, `LastDeliverySummary` на основе Postfix events.

План:

- сопоставлять Postfix событие с `send_events`;
- сначала искать по `ProviderMessageId`/queue id, затем осторожно по recipient и временному окну;
- `sent` маппить в `Delivered`;
- `deferred` маппить в `SoftBounce`;
- `bounced` и `expired` маппить в `HardBounce`;
- применять уже существующую логику приоритета `ApplyDeliveryStatus`.

## Sprint 5.6.5 - UI delivery diagnostics

Статус: **запланирован**.

Цель: показать пользователю и администратору реальные статусы доставки и причины отказов.

План:

- добавить в ЛК строки: `Доставлено`, `Временные ошибки`, `Недоставлено`;
- в таблице получателей показывать статус доставки и диагностический текст;
- в админке добавить блок Postfix diagnostics;
- не показывать пользователю сырые технические данные без нормальной русской формулировки.

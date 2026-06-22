# Delivery status rollout plan

Документ фиксирует план внедрения реальных статусов доставки на основе Postfix logs.

## Статус на 22 июня 2026 г.

- Sprint 5.6.1 - закрыт: добавлен изолированный parser строк `postfix/smtp`, без БД и без влияния на отправку писем.
- Sprint 5.6.2 - закрыт: добавлены доменная модель, SQL-скрипт, EF/InMemory репозитории и таблица `postfix_delivery_events`.
- Sprint 5.6.3 - закрыт: добавлен сервис ingestion, который принимает строки Postfix-лога, парсит их и сохраняет новые события в `postfix_delivery_events`.
- Sprint 5.6.4 - закрыт: ingestion service применяет delivery status к `send_events`, если событие можно сопоставить по `ProviderMessageId = Postfix queue id`.
- Sprint 5.6.5 - закрыт: локальный SMTP-адаптер извлекает Postfix queue id из ответа `queued as ...` и сохраняет его как provider message id, с fallback на `Message-ID`.
- Sprint 5.6.6 - закрыт: reader вручную читает `/var/log/mail.log` с cursor-позиции, сохраняет Postfix delivery events и обновляет `send_events.DeliveryStatus`; production-проверка подтвердила обновление до `Delivered`.

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
- поддержка классического syslog-формата `Jun 22 ...`;
- поддержка ISO-формата `2026-06-22T...`, который используется на production-сервере;
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

Статус: **закрыт**.

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

Статус: **закрыт**.

Цель: обновлять существующие поля `send_events.DeliveryStatus`, `LastDeliveryEventAt`, `LastDeliverySummary` на основе Postfix events.

Входит:

- сопоставление Postfix event с `send_events` по `ProviderMessageId = Postfix queue id`;
- `sent` маппится в `Delivered`;
- `deferred` маппится в `SoftBounce`;
- `bounced` и `expired` маппятся в `HardBounce`;
- применяется уже существующая логика приоритета `ApplyDeliveryStatus`;
- добавлены счётчики `MatchedSendEvents` и `UpdatedSendEvents`;
- тест проверяет применение delivery status к matching `send_event`.

Результат Sprint 5.6.4: приложение умеет применять реальные delivery statuses к `send_events`, если Postfix queue id уже сохранён в `ProviderMessageId`.

## Sprint 5.6.5 - Capture Postfix queue id

Статус: **закрыт**.

Цель: при успешной локальной SMTP-отправке сохранять Postfix queue id из SMTP-ответа, чтобы последующие события из `/var/log/mail.log` можно было точно сопоставить с `send_events`.

Входит:

- извлечение queue id из SMTP-ответа вида `queued as ABC123`;
- сохранение queue id в `ProviderMessageId` для успешных SMTP-отправок;
- fallback на `Message-ID`, если queue id не удалось получить;
- тесты на извлечение queue id;
- production-проверка: новые `send_events.ProviderMessageId` содержат Postfix queue id, которые находятся в `/var/log/mail.log`.

Результат Sprint 5.6.5: новые отправки через локальный Postfix получают `ProviderMessageId`, равный Postfix queue id. Это открывает путь к автоматическому сопоставлению строк `postfix/smtp` с `send_events`.

## Sprint 5.6.6 - Postfix log reader and manual admin run

Статус: **закрыт**.

Цель: читать новые строки из Postfix log с cursor-позиции и запускать это чтение вручную из админки до включения фонового режима.

Входит:

- `PostfixDeliveryLogReaderService`;
- чтение файла лога с последней сохранённой позиции;
- cursor-файл;
- безопасная инициализация cursor на конец файла при первом запуске;
- обработка ротации, когда сохранённая позиция больше текущего размера файла;
- передача новых строк в ingestion service;
- тесты на инициализацию cursor, чтение только новых строк и применение delivery status к matching `send_event`;
- DI-регистрация reader-а и его production-настроек;
- админский ручной endpoint `/admin/delivery/postfix`;
- POST `/admin/delivery/postfix/read`, который показывает счётчики чтения, парсинга, сохранения и обновления `send_events`;
- production-проверка: новое письмо с `ProviderMessageId = CD71284177` было найдено по Postfix queue id и обновлено до `DeliveryStatus = Delivered`.

Результат Sprint 5.6.6: приложение вручную читает `/var/log/mail.log` безопасно и инкрементально, сохраняет delivery events и обновляет `send_events`. Автозапуск по расписанию будет отдельным шагом.

## Sprint 5.6.7 - Background Postfix log reader

Статус: **запланирован**.

Цель: включить регулярное чтение Postfix log без ручного запуска.

План:

- выбрать безопасный способ запуска: Hangfire job, hosted service или systemd timer;
- запускать reader с ограниченной частотой;
- логировать результат запуска;
- не падать при временной недоступности лога;
- сохранить возможность ручного запуска из админки.

## Sprint 5.6.8 - UI delivery diagnostics

Статус: **запланирован**.

Цель: показать пользователю и администратору реальные статусы доставки и причины отказов.

План:

- добавить в ЛК строки: `Доставлено`, `Временные ошибки`, `Недоставлено`;
- в таблице получателей показывать статус доставки и диагностический текст;
- в админке добавить блок Postfix diagnostics;
- не показывать пользователю сырые технические данные без нормальной русской формулировки.

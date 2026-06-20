# Sprint 9 — ответы получателей и пересылка клиенту

Ветка: `Development`  
Основание: `docs/sprints.md`, раздел «Спринт 9 — ответы получателей и пересылка клиенту»  
Дата актуализации: 2026-06-20  
Статус: backlog для реализации

## 1. Текущий срез кода

По состоянию ветки `Development` перед Sprint 9:

- Sprint 6 должен дать отправку через очередь, fake email provider, `IEmailProviderAdapter`, `SendEvent`, `ProviderMessageId`, progress и summary отправки;
- Sprint 7 должен дать публичную глобальную отписку, `GlobalSuppression`, unsubscribe token и повторную проверку suppression перед отправкой;
- Sprint 8 должен дать delivery/bounce/complaint webhooks, историю provider events, fake webhook sender, обработку `hard_bounce` и `complaint`;
- архитектурно для ответов уже предусмотрены модуль `Inbound`, очередь `reply` и cleanup-задачи;
- обработка входящих ответов не должна смешиваться с delivery webhooks: delivery webhooks описывают состояние доставки, inbound reply — это новое пользовательское сообщение от получателя;
- пользовательский результат Sprint 9 — клиент видит счётчик ответов в ЛК, а fake provider/dev-интерфейс показывает, что ответ переслан клиенту;
- тела ответов и вложения нельзя хранить бессрочно: Sprint 9 обязан ввести ограниченное хранение и cleanup job;
- реальные ESP inbound routes пока не подключаются: Sprint 9 работает через fake inbound webhook/provider, но контракт должен быть готов к реальному провайдеру.

Sprint 9 продолжает основной MVP-flow после фактической отправки письма, а не создаёт отдельную helpdesk/CRM-систему.

## 2. Цель Sprint 9

Адресат отвечает на письмо, сервис принимает входящий ответ, сопоставляет его с рассылкой и получателем, сохраняет технический факт ответа и пересылает письмо клиенту через provider adapter.

Рабочий сценарий Sprint 9:

```text
создать рассылку → отправить через fake provider → fake provider формирует входящий reply → inbound webhook принимает payload → система определяет mailing/recipient/client → создаёт ReplyEvent → пересылает ответ клиенту через fake provider → клиент видит счётчик ответов → cleanup удаляет тело/вложения после срока хранения, но оставляет технический лог
```

## 3. Обязательные рамки Sprint 9

- Входящий reply — отдельный тип события, не delivery webhook и не complaint.
- Reply matching должен поддерживать несколько способов идентификации:
  - `Reply-To` alias/token;
  - campaign/mailing token;
  - recipient token или stable recipient key;
  - служебные заголовки, если они есть в fake/real provider payload.
- Нельзя создавать рассылку, получателя или клиента только из входящего письма, если сопоставление не найдено.
- Несопоставленный reply нужно безопасно логировать как unmatched, не раскрывая лишние данные клиенту.
- Пересылка клиенту должна идти через `IEmailProviderAdapter`/provider abstraction, а не напрямую из endpoint.
- Нужно предотвратить mail loop: не пересылать системные автоответы обратно в бесконечную цепочку.
- Тело ответа хранится ограниченно; технический лог (`ReplyEvent`) остаётся после cleanup.
- Вложения в Sprint 9 допускаются как metadata-only или безопасное временное хранение, но не как полноценный файловый архив.
- UI показывает простой счётчик/индикатор ответов, а не полноценный почтовый клиент.
- Пользовательский язык интерфейса — русский.
- В UI клиента не показывать внутренние токены, provider payload, raw headers и адреса других клиентов.

## 4. Задачи реализации

### T9-01. Спроектировать inbound identity и Reply-To alias

**Цель:** сделать ответы получателей сопоставимыми с конкретной рассылкой и адресатом.

Задачи:

- Определить формат reply identity для MVP:
  - `mailingId` / public mailing id;
  - stable recipient key;
  - client id;
  - purpose/version;
  - подпись/HMAC/Data Protection payload.
- Добавлять в исходящее письмо технический `Reply-To` alias/token или provider metadata, достаточные для будущего сопоставления.
- Не полагаться только на тему письма: пользователь может её поменять при ответе.
- Не полагаться только на `From` адрес получателя: он может быть изменён/форварднут.
- Если в Sprint 6/8 уже есть provider metadata, расширить её аккуратно, без переименования существующих полей без необходимости.
- Для fake provider описать явный формат `replyToken` / `inboundAddress`.

Acceptance criteria:

- Каждое отправленное письмо получает технический идентификатор, пригодный для inbound reply matching.
- Токен нельзя просто подобрать или подменить без подписи.
- Формат токена версионирован.
- Реализация не зависит от конкретного реального ESP.

### T9-02. Добавить доменную модель `ReplyEvent`

**Цель:** устойчиво хранить факт входящего ответа и его processing status.

Задачи:

- Добавить сущность `ReplyEvent` или аналог.
- Минимальные поля:
  - `Id`;
  - `Provider`;
  - `ProviderInboundEventId`;
  - `MailingId` nullable для unmatched;
  - `ClientId` nullable для unmatched;
  - `RecipientEmailNormalized` или stable recipient key;
  - `FromEmailNormalized`;
  - `ToAddress` / inbound alias;
  - `ReplyTokenHash` или безопасный technical key;
  - `SubjectNormalized` / safe subject preview;
  - `ReceivedAt`;
  - `ProcessedAt` nullable;
  - `ForwardedAt` nullable;
  - `ProcessingStatus` (`processed`, `forwarded`, `unmatched`, `ignored_auto_reply`, `duplicate`, `failed`);
  - `BodyStorageStatus` (`stored_temporarily`, `redacted`, `deleted`, `not_stored`);
  - `BodyExpiresAt` nullable;
  - `RawPayloadHash`;
  - `ErrorCode` / `ErrorMessage` для диагностики.
- Добавить уникальность по `Provider + ProviderInboundEventId`.
- Не хранить raw payload без явной необходимости; если хранится — только безопасно и временно.
- Не хранить лишние персональные данные в логах, где достаточно hash/normalized preview.

Acceptance criteria:

- Повторный inbound webhook не создаёт дубль `ReplyEvent`.
- ReplyEvent можно связать с mailing/client/recipient при успешном matching.
- Unmatched reply сохраняется как технический факт без создания лишних доменных сущностей.
- После cleanup технический лог остаётся, а тело/вложения удаляются или редактируются.

### T9-03. Добавить persistence и repository для входящих ответов

**Цель:** сделать входящие ответы устойчивыми после рестарта приложения.

Задачи:

- Добавить `IReplyEventRepository` или аналог.
- Минимальные методы:
  - `AddIfNotExistsAsync(...)`;
  - `GetByProviderEventIdAsync(provider, providerInboundEventId)`;
  - `CountByMailingAsync(mailingId)`;
  - `ListRecentByMailingAsync(mailingId, limit)` для UI/admin/dev;
  - `FindExpiredBodiesAsync(now, batchSize)` для cleanup;
  - `MarkBodyDeletedAsync(replyEventId)`.
- Реализовать EF Core/PostgreSQL persistence.
- Добавить `DbSet`, configuration и миграцию.
- Индексы:
  - `Provider + ProviderInboundEventId` unique;
  - `MailingId + ReceivedAt`;
  - `ClientId + ReceivedAt`;
  - `BodyExpiresAt` для cleanup.
- In-memory реализация допустима только для unit-тестов/dev fallback.

Acceptance criteria:

- ReplyEvent сохраняется в БД.
- Повторный webhook идемпотентен.
- Счётчик ответов по рассылке строится из persistence, а не из HTML.
- Cleanup может эффективно найти устаревшие тела.

### T9-04. Расширить `IEmailProviderAdapter` inbound-контрактом

**Цель:** endpoint не должен знать формат конкретного провайдера.

Задачи:

- Добавить provider-level DTO/value object `InboundEmailMessage` / `EmailProviderInboundEvent`.
- Добавить методы/контракты:
  - `ParseInboundWebhookAsync(...)`;
  - `ValidateInboundWebhookSignatureAsync(...)`, если защита включается уже сейчас;
  - `ForwardReplyToClientAsync(...)` или отдельный outbound method для пересылки.
- Для fake provider описать простой JSON payload:
  - `providerInboundEventId`;
  - `from`;
  - `to`;
  - `replyToken`;
  - `subject`;
  - `textBody`;
  - `htmlBody` optional;
  - `headers`;
  - `attachments` metadata optional.
- Ошибки парсинга возвращать как controlled result, а не непойманное исключение.
- Provider-specific названия маппить в доменные поля явно.

Acceptance criteria:

- Inbound endpoint работает с нормализованным provider DTO.
- Fake provider может сгенерировать валидный reply payload.
- Invalid payload безопасно отклоняется/логируется.
- Пересылка клиенту вызывается через provider abstraction.

### T9-05. Добавить inbound webhook endpoints

**Цель:** принимать входящие ответы через HTTP в fake-provider режиме.

Задачи:

- Добавить `MapInboundEndpoints()` или `MapReplyEndpoints()`.
- Явно подключить endpoints в `Program.cs`.
- Минимальный endpoint:
  - `POST /webhooks/email/fake/inbound`.
- Опционально provider-agnostic route:
  - `POST /webhooks/email/{provider}/inbound`.
- Endpoint должен:
  - принять raw body;
  - определить provider;
  - проверить secret/signature, если включено;
  - передать payload в adapter/parser;
  - вызвать application service обработки inbound reply;
  - вернуть безопасный ответ без внутренних деталей.
- Защитить dev/fake endpoint через config flag, secret header, environment guard или admin-only dev UI.
- Не отдавать raw payload, stack trace или внутренние токены наружу.

Acceptance criteria:

- Валидный fake inbound webhook создаёт `ReplyEvent`.
- Повторный webhook не создаёт дубль.
- Невалидный payload возвращает безопасный ответ.
- Endpoint подключён в `Program.cs`.

### T9-06. Реализовать service сопоставления входящего ответа

**Цель:** определить, к какой рассылке и получателю относится входящее письмо.

Задачи:

- Добавить сервис `InboundReplyMatchingService` или аналог.
- Алгоритм matching:
  1. попытаться проверить подписанный `replyToken`;
  2. fallback по `Reply-To` alias;
  3. fallback по служебным заголовкам;
  4. fallback по provider metadata, если есть;
  5. если ничего не найдено — `unmatched`.
- Проверять, что найденная рассылка действительно принадлежит найденному клиенту.
- Проверять, что recipient key/email относится к этой рассылке.
- Не создавать recipient из входящего письма.
- Не доверять неподписанным значениям без валидации.
- Сохранять причину unmatched для admin/dev диагностики без раскрытия пользователю.

Acceptance criteria:

- Валидный replyToken определяет mailing/client/recipient.
- Подделанный/испорченный токен не создаёт связь.
- Несопоставленный reply сохраняется как unmatched.
- Сервис покрыт unit-тестами по всем fallback-веткам.

### T9-07. Реализовать обработку и пересылку ответа клиенту

**Цель:** клиент получает ответ адресата через сервис, не заходя в отдельный почтовый клиент внутри MVP.

Задачи:

- Добавить application service `InboundReplyProcessingService`.
- Сервис должен:
  - обеспечить идемпотентность по `Provider + ProviderInboundEventId`;
  - вызвать matching service;
  - создать/обновить `ReplyEvent`;
  - подготовить безопасное forwarded-письмо клиенту;
  - вызвать `IEmailProviderAdapter` для пересылки;
  - отметить `ForwardedAt` и `ProcessingStatus`.
- Forwarded-письмо клиенту должно содержать:
  - понятную тему, например `Ответ на рассылку: <тема>`;
  - email отправителя-адресата;
  - дату получения;
  - название/тему рассылки;
  - безопасный текст ответа;
  - предупреждение, что это пересланный ответ через сервис.
- Не добавлять unsubscribe link в пересылку клиенту, если это служебное письмо клиенту, а не массовая рассылка.
- Ограничить размер пересылаемого тела.
- Не пересылать опасные/исполняемые вложения.

Acceptance criteria:

- Валидный reply пересылается клиенту через fake provider.
- `ReplyEvent` получает статус `forwarded`.
- Ошибка пересылки не теряет входящий reply и помечается как `failed`/retryable.
- Пользователь не видит внутренние токены и provider payload.

### T9-08. Добавить защиту от auto-reply и mail loops

**Цель:** не породить бесконечную переписку и шум от автоответчиков.

Задачи:

- Добавить распознавание auto-reply признаков:
  - `Auto-Submitted`;
  - `Precedence: bulk/list/junk`;
  - `X-Auto-Response-Suppress`;
  - provider-specific headers из fake payload;
  - пустой/подозрительный sender;
  - совпадение с системными адресами сервиса.
- Для auto-reply:
  - создавать `ReplyEvent` со статусом `ignored_auto_reply` или аналогом;
  - не пересылать клиенту по умолчанию;
  - учитывать в admin/dev диагностике.
- Предотвратить пересылку писем, пришедших от собственного service-domain, если они выглядят как loop.
- Ограничить частоту пересылок по одному mailing/recipient, если потребуется простой rate limit.

Acceptance criteria:

- Auto-reply не пересылается клиенту.
- Технический факт auto-reply сохраняется.
- Повторные auto-reply не создают дубли и не ломают счётчики.
- Unit-тесты покрывают типовые headers auto-reply.

### T9-09. Реализовать ограниченное хранение тела и вложений

**Цель:** выполнить MVP-требование: тело ответа хранится ограниченно, технический лог остаётся.

Задачи:

- Задать конфигурацию срока хранения тела ответа, например `InboundReplies:BodyRetentionDays`.
- Для MVP выбрать безопасное значение по умолчанию, например 7 или 14 дней, зафиксировав его в конфиге/документации.
- Хранить тело ответа отдельно от основного `ReplyEvent` или явно помечать `BodyExpiresAt`.
- Вложения:
  - либо не хранить в Sprint 9;
  - либо хранить только metadata;
  - либо временно хранить в safe storage с отдельным TTL.
- Не сохранять потенциально опасные вложения без проверки типа/размера.
- Добавить redaction/обрезку слишком больших тел.

Acceptance criteria:

- У каждого сохранённого тела есть `BodyExpiresAt`.
- Большое тело обрезается или отклоняется по контролируемой политике.
- Вложения не становятся бессрочным хранилищем файлов.
- После cleanup основной `ReplyEvent` остаётся доступен для статистики.

### T9-10. Добавить cleanup job для inbound replies

**Цель:** автоматически удалять тело ответа и временные вложения после срока хранения.

Задачи:

- Добавить Hangfire job / hosted cleanup job в очередь `cleanup`.
- Job должен:
  - находить expired reply bodies;
  - удалять/редактировать body storage;
  - удалять временные attachments/metadata по политике;
  - ставить `BodyStorageStatus = deleted`;
  - сохранять технический audit/log;
  - быть идемпотентным.
- Добавить ручной dev/admin trigger для проверки cleanup, если это принято в текущем dev UI.
- Не удалять сам `ReplyEvent` при удалении тела.

Acceptance criteria:

- Cleanup можно безопасно запустить повторно.
- Тело ответа удаляется после TTL.
- Счётчик ответов и технический лог не пропадают.
- Ошибка удаления одного body не останавливает весь batch.

### T9-11. Добавить UI-счётчик ответов и dev/admin диагностику

**Цель:** пользователь видит, что на рассылку пришли ответы, без полноценного почтового интерфейса.

Задачи:

- В summary/карточку рассылки добавить показатель «Ответы».
- На странице рассылки показать простой блок:
  - количество ответов;
  - время последнего ответа;
  - статус последней пересылки, если нужно.
- Не показывать клиенту raw тело ответа в ЛК, если принято решение пересылать ответы только на email клиента.
- Для dev/admin добавить диагностический список последних `ReplyEvent`:
  - mailing;
  - status;
  - matched/unmatched;
  - forwarded/failed;
  - cleanup status.
- UI на русском языке.
- Не показывать внутренние provider ids и tokens обычному клиенту.

Acceptance criteria:

- После fake reply счётчик ответов увеличивается.
- Клиент видит понятный статус, что ответ переслан.
- Admin/dev может проверить, почему reply unmatched или failed.
- UI не превращается в полноценный inbox.

### T9-12. Добавить тесты Sprint 9

**Цель:** зафиксировать безопасное поведение inbound reply flow.

Unit-тесты:

- Определение рассылки по reply token.
- Невалидный reply token даёт unmatched/invalid результат.
- Создание `ReplyEvent`.
- Идемпотентность повторного inbound webhook.
- Пересылка клиенту через provider adapter.
- Auto-reply не пересылается клиенту.
- Cleanup удаляет тело, но оставляет технический лог.
- Неизвестный/unmatched reply безопасно логируется.
- Ограничение размера тела ответа.

Интеграционные тесты:

- Fake inbound webhook → `ReplyEvent`.
- Fake inbound webhook → matching по token → forward to client.
- Повторный webhook не создаёт дубль и не пересылает повторно.
- Unmatched reply сохраняется без связи с рассылкой.
- Auto-reply игнорируется для пересылки.
- Cleanup job удаляет body/attachments и оставляет `ReplyEvent`.

Ручные тесты:

- Отправить рассылку через fake provider.
- Создать fake reply из dev/fake provider UI.
- Проверить, что ответ появился в статистике рассылки.
- Проверить, что клиенту создана fake-пересылка.
- Повторить тот же inbound event и убедиться, что дубля нет.
- Отправить auto-reply payload и убедиться, что клиенту он не переслан.
- Запустить cleanup и убедиться, что тело удалено, а лог остался.

## 5. Definition of Done

Sprint 9 считается завершённым, если:

- есть защищённый и версионированный способ сопоставить входящий ответ с рассылкой/получателем;
- fake inbound webhook принимает ответ и создаёт `ReplyEvent`;
- валидный ответ пересылается клиенту через fake provider/provider adapter;
- повторный inbound webhook идемпотентен;
- unmatched reply безопасно логируется и не создаёт лишние сущности;
- auto-reply/mail loop не пересылаются клиенту;
- тело ответа хранится ограниченно и удаляется cleanup job;
- счётчик ответов виден в UI рассылки;
- unit, integration и ручные тесты Sprint 9 описаны и реализованы;
- пользовательские тексты на русском языке;
- внутренние токены, provider payload и технические id не раскрываются обычному клиенту.

## 6. Документы и разделы для синхронизации

После реализации Sprint 9 нужно синхронизировать:

- `docs/sprints.md` — отметить фактический статус Sprint 9 и уточнить отклонения от плана;
- `docs/architecture_dotnet.md` — обновить описание модуля `Inbound`, очереди `reply`, cleanup и provider adapter;
- `docs/platform_tz.md` — если техническое ТЗ ведётся как основной документ чата «Техника», добавить inbound webhook, reply matching, forwarding и retention policy;
- `docs/specification.md`, Раздел C «Юридические аспекты» — согласовать срок хранения тел ответов/вложений и минимизацию персональных данных;
- `docs/specification.md`, Раздел A «Общее» — при необходимости уточнить пользовательское обещание: ответы получателей приходят клиенту через пересылку сервиса.

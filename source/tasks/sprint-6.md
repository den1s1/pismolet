# Sprint 6 — очередь отправки и fake email provider

Ветка: `Development`  
Основание: `docs/sprints.md`, раздел «Спринт 6 — очередь отправки и fake email provider»  
Дата составления: 2026-06-18  
Статус: backlog для реализации

## 1. Текущий срез кода

По состоянию ветки `Development` перед Sprint 6:

- приложение уже собрано как .NET / ASP.NET Core vertical slice с проектами `Pismolet.Domain`, `Pismolet.Application`, `Pismolet.Infrastructure`, `Pismolet.Web`, `Pismolet.Web.Tests`, `Pismolet.IntegrationTests`;
- в `Program.cs` уже подключены payment, check и admin endpoints: `MapPaymentEndpoints()`, `MapCheckEndpoints()`, `MapAdminEndpoints()`;
- Sprint 4 flow уже ведёт пользователя к fake-оплате и статусу «Оплачено»;
- Sprint 5 flow уже содержит формальную проверку, статусы «Проверяем перед отправкой», «Одобрено», «На ручной проверке», «Отклонено», admin moderation queue и approve/reject;
- после одобрения в UI пока показана заглушка «Отправка — Sprint 6»;
- домен клиента уже содержит `DailySendLimit`, `TotalSendLimit`, `PremoderationRequired`;
- `IEmailProviderAdapter`, fake email provider, `SendEvent`, очередь отправки, batch-отправка, статистика отправки и endpoints запуска отправки ещё не реализованы;
- постоянное хранилище проекта уже ориентировано на EF Core/PostgreSQL; для Sprint 6 события отправки и итоговая статистика должны быть устойчивыми после рестарта приложения.

Sprint 6 должен продолжать существующий flow, а не начинать отправку отдельным сценарием.

## 2. Цель Sprint 6

Одобренная рассылка проходит через очередь отправки и fake email provider. Пользователь запускает оплаченную и одобренную рассылку, видит прогресс и итоговую статистику:

- принято к отправке;
- отправлено;
- ошибка;
- отписано / исключено по global suppression;
- приостановлено по дневному лимиту.

Sprint 6 должен завершиться работающим сценарием:

```text
создать рассылку → загрузить CSV → подтвердить базу → написать письмо → оплатить fake-оплатой → пройти проверку → получить «Одобрено» → запустить отправку → увидеть progress → получить итог «Отправлено» / «Есть ошибки» / «Приостановлено»
```

## 3. Обязательные рамки Sprint 6

- Отправка в Sprint 6 идёт только через fake email provider.
- Реальные SMTP/API-провайдеры, DKIM/SPF/DMARC и webhook доставки не подключаются.
- Отправка должна идти через Hangfire job, а не выполняться целиком внутри HTTP request.
- В очередь передаются только идентификаторы: `mailingId`, `batchId`/`jobId`, при необходимости `recipientId`; не передавать тело письма или весь список адресов.
- Отправка доступна только из технического состояния `approved`.
- Из `paid`, `pending_checks`, `review_required`, `rejected`, `sent`, `failed` отправку запускать нельзя.
- Из `paused` разрешено продолжение отправки только через отдельный сценарий resume, если причина паузы снята.
- Повторный запуск отправки должен быть идемпотентным и не должен создавать дубли писем.
- Перед фактической отправкой нужно повторно учитывать global suppression, даже если адрес был принят при импорте.
- Дневной лимит клиента применяется на уровне постановки/выполнения отправки.
- Дневной лимит должен быть изменяемым администратором в рамках Sprint 6.
- Batch-отправка обязательна. Отдельная сущность `SendBatch`/`SendJob` допустима, но не обязательна, если batch-состояние надёжно восстанавливается через `SendEvent` и статус рассылки.
- EF Core/PostgreSQL persistence для `SendEvent` и итоговой статистики обязателен. In-memory реализации допустимы только для unit-тестов и dev fallback.
- UI остаётся на текущем подходе minimal endpoints + `HtmlRenderer`.
- Пользовательский язык интерфейса — русский.
- В UI не показывать внутренние термины `SendEvent`, `providerMessageId`, `jobId`, `batchId`, если это не dev/admin экран.

## 4. Задачи реализации

### T6-01. Ввести стабильную техническую статусную модель рассылки

**Цель:** убрать зависимость отправки от русских строк и обеспечить надёжные переходы после Sprint 5.

Задачи:

- Ввести или использовать единый `MailingStatus` enum/value object/status-code для машинной логики рассылки.
- Минимальный набор статусов, нужных после Sprint 6:
  - `draft`;
  - `recipients_imported`;
  - `declaration_confirmed`;
  - `message_prepared`;
  - `priced`;
  - `payment_pending`;
  - `paid`;
  - `pending_checks`;
  - `review_required`;
  - `approved`;
  - `rejected`;
  - `sending`;
  - `sent`;
  - `failed`;
  - `paused`.
- Русские подписи хранить отдельно: как computed label/helper/mapper, но не использовать `StatusRu` как единственный источник бизнес-логики.
- Если в текущем коде уже есть `StatusRu`, сохранить обратную совместимость UI через маппинг, но новые переходы Sprint 6 делать через технический статус.
- Описать допустимые переходы для отправки:
  - `approved` → `sending`;
  - `sending` → `sent`;
  - `sending` → `failed`;
  - `sending` → `paused`;
  - `paused` → `sending` после снятия причины паузы;
  - `paused` → `sent`, если после продолжения все оставшиеся письма отправлены.
- Запретить старт отправки из любых статусов, кроме `approved` и отдельного resume из `paused`.

Acceptance criteria:

- Рассылка без технического статуса `approved` не может быть поставлена в отправку.
- Отклонённая рассылка не может быть отправлена.
- Повторный refresh страницы не меняет статус и не запускает отправку повторно.
- В коде Sprint 6 нет новых проверок бизнес-статуса через русские строки.

### T6-02. Добавить доменные модели отправки и batch-механику

**Цель:** фиксировать результат отправки по каждому адресу, поддержать batch-отправку и строить статистику.

Задачи:

- Добавить модель `SendEvent` или аналог.
- Минимальные поля `SendEvent`:
  - `Id`;
  - `MailingId`;
  - `RecipientId` и/или нормализованный `RecipientEmail`;
  - `Status` (`Pending`, `Accepted`, `Failed`, `Skipped`, `Paused`);
  - `SkipReason` / `Reason` для пропусков, минимум `GlobalSuppression`, `DailyLimit`, `AlreadySent`;
  - `Provider`;
  - `ProviderMessageId`;
  - `Attempt`;
  - `ErrorCode`;
  - `ErrorMessage`;
  - `CreatedAt`;
  - `UpdatedAt`.
- Добавить модель summary/result, например `MailingSendSummary`:
  - `AcceptedForSending` / принято к отправке;
  - `Sent` / отправлено;
  - `Failed` / ошибка;
  - `Suppressed` / отписано или исключено по global suppression;
  - `PausedByLimit` / приостановлено по дневному лимиту;
  - `SkippedOther` / прочие пропуски, если нужны;
  - `TotalAcceptedRecipients`.
- Реализовать batch-механику:
  - отправлять получателей порциями, а не одной неограниченной операцией;
  - размер batch вынести в конфигурацию, например `Sending:BatchSize`;
  - default batch size для MVP может быть небольшим, например 100;
  - batch должен уважать дневной лимит и global suppression.
- Отдельная сущность `SendBatch`/`SendJob` допускается, если упрощает восстановление прогресса.
- Если отдельная сущность добавляется, хранить в ней:
  - `Id`;
  - `MailingId`;
  - `Status`;
  - `CreatedAt`;
  - `StartedAt`;
  - `FinishedAt`;
  - `RequestedBy`;
  - `Cursor`/`LastProcessedRecipientId`, если выбран cursor-подход.

Acceptance criteria:

- По рассылке можно восстановить, кому fake provider «отправил» письмо, кто получил ошибку, кто был исключён по отписке/global suppression, кто приостановлен лимитом.
- Provider message id сохраняется для успешных fake-отправок.
- Итоговая статистика не считается только из HTML.
- Batch-отправка реально ограничивает размер одной порции обработки.

### T6-03. Добавить persistence-контракты и EF/PostgreSQL-хранилище отправки

**Цель:** отделить бизнес-логику отправки от хранилища и сохранить события отправки устойчиво.

Задачи:

- Добавить интерфейсы, например:
  - `ISendEventRepository`;
  - `ISendBatchRepository`, если выбран batch/job entity;
  - `IMailingSendSummaryReader`, если summary отделяется от repository.
- Реализовать EF Core/PostgreSQL persistence для `SendEvent`.
- Добавить `DbSet`, entity configuration и EF migration для таблицы `send_events`.
- Если используется `SendBatch`/`SendJob`, добавить EF Core persistence и migration для этой сущности.
- Реализовать in-memory версии только для unit-тестов и dev fallback; они не считаются достаточным результатом Sprint 6.
- Добавить уникальное ограничение, предотвращающее дубли отправки одного recipient в одной рассылке, например по `MailingId + RecipientId` или `MailingId + NormalizedEmail`.
- Добавить методы:
  - получить events по `MailingId`;
  - получить event по `MailingId + RecipientId/Email`;
  - upsert/save event;
  - посчитать summary;
  - получить pending recipients для продолжения;
  - получить failed recipients для повторной обработки, если retry входит в Sprint 6;
  - получить количество событий за текущий день для лимита.

Acceptance criteria:

- Повторный запуск отправки не создаёт дублирующие `SendEvent` для одного recipient.
- Summary строится через application service/repository, а не через endpoint.
- После рестарта dev-приложения с PostgreSQL события отправки и итоговая статистика сохраняются.
- Миграции применяются в development flow так же, как остальные persistence-изменения проекта.

### T6-04. Реализовать контракт email provider adapter

**Цель:** подготовить слой, который позже можно заменить реальным email-провайдером.

Задачи:

- Добавить интерфейс `IEmailProviderAdapter`.
- Минимальный контракт:
  - `SendAsync(EmailMessage message, CancellationToken cancellationToken)`;
  - возврат `EmailProviderSendResult`.
- Добавить DTO/value objects:
  - `EmailMessage`;
  - `EmailRecipient`;
  - `EmailProviderSendResult`.
- `EmailMessage` должен содержать:
  - recipient email;
  - sender name;
  - subject;
  - plain text body;
  - service headers/metadata, если нужно для fake provider;
  - unsubscribe URL для конкретного recipient.
- Не смешивать provider adapter с rendering service и HTTP endpoints.
- Не передавать provider adapter полную рассылку или полный список recipients, только подготовленное сообщение для конкретного recipient/batch item.

Acceptance criteria:

- Application service зависит от `IEmailProviderAdapter`, а не от конкретного fake provider.
- Контракт можно покрыть unit-тестами без web-сервера.
- В Sprint 6 нет прямой SMTP/API-интеграции.

### T6-05. Реализовать fake email provider

**Цель:** имитировать результат отправки без внешней интеграции.

Задачи:

- Добавить `FakeEmailProviderAdapter`.
- Для успешной отправки возвращать стабильный `ProviderMessageId`, например `fake-{mailingId}-{recipientHash}`.
- Добавить режимы fake-ошибок для тестов:
  - адрес содержит `fail` → provider возвращает ошибку;
  - адрес содержит `temp` → временная ошибка, если будет реализован retry;
  - остальные адреса → accepted/sent.
- Не отправлять реальные письма через fake provider.
- Логировать результат на уровне `SendEvent`, а не только в console.
- Fake provider должен быть детерминированным, чтобы integration tests не зависели от случайности.

Acceptance criteria:

- Fake provider accepted создаёт успешный `SendEvent`.
- Fake provider error создаёт failed `SendEvent`.
- Повторная отправка уже успешного recipient не создаёт второй provider message id.

### T6-06. Настроить очередь отправки через Hangfire

**Цель:** вынести отправку из HTTP request и приблизить MVP к production-flow.

Задачи:

- Добавить пакеты Hangfire, совместимые с текущей версией .NET.
- Настроить Hangfire в `Program.cs`/DI.
- Hangfire обязателен для пользовательского запуска отправки.
- Для dev окружения использовать PostgreSQL storage для Hangfire, если совместимо с текущей БД и не блокирует спринт.
- Если Hangfire PostgreSQL storage технически блокирует Sprint 6, допустим временный in-memory storage только как dev fallback, но:
  - это должно быть явно описано в README;
  - события `SendEvent` всё равно сохраняются в EF/PostgreSQL;
  - в `docs/platform_tz.md` после реализации нужно зафиксировать выбранный storage и ограничение.
- Добавить background job, например `SendMailingJob`.
- В очередь передавать только ID:
  - `mailingId`;
  - опционально `batchId`.
- Обеспечить идемпотентность job:
  - если рассылка уже `sent`, job ничего не делает;
  - если recipient уже имеет success-event, повторно не отправлять;
  - если рассылка `rejected`, job ничего не отправляет;
  - если job запущен повторно параллельно, дубли не создаются за счёт repository/unique constraint.

Acceptance criteria:

- Нажатие «Запустить отправку» быстро возвращает страницу статуса, а не держит HTTP request до конца всей рассылки.
- Повторный запуск job не дублирует отправку.
- В тестах можно выполнить job синхронно через application service без реального Hangfire dashboard.
- Отсутствие Hangfire в пользовательском flow не допускается как завершённое состояние Sprint 6.

### T6-07. Реализовать application service запуска и выполнения отправки

**Цель:** отделить orchestration отправки от HTTP endpoints.

Задачи:

- Добавить сервис, например `IMailingSendService` / `MailingSendService`.
- Метод `StartSending` должен:
  - проверить владельца рассылки;
  - проверить, что рассылка оплачена;
  - проверить, что рассылка одобрена через технический `MailingStatus.Approved`;
  - проверить наличие сохранённого сообщения;
  - получить accepted recipients;
  - повторно исключить global suppression перед отправкой;
  - применить дневной лимит;
  - создать pending/send events;
  - перевести рассылку в `sending` или `paused`;
  - поставить job в очередь через Hangfire.
- Метод выполнения job должен:
  - брать pending recipients batch-ами;
  - строить письмо для каждого recipient;
  - вызывать `IEmailProviderAdapter`;
  - сохранять результат в `SendEvent`;
  - обновлять summary/status рассылки;
  - завершать рассылку статусом `sent`, если все допустимые recipients обработаны успешно или пропущены по suppression;
  - переводить в `failed`, если есть критическая ошибка, из-за которой job не может продолжаться;
  - переводить в `paused`, если достигнут дневной лимит.
- Ошибки бизнес-сценариев возвращать как application result, а не обычные исключения.

Acceptance criteria:

- Endpoint не содержит логики provider, лимитов и idempotency.
- Отправку можно покрыть unit/integration тестами без HTML.
- Рассылка без оплаты или без approve не запускается.
- После fake-отправки статус и summary восстанавливаются из application/persistence слоя.

### T6-08. Реализовать batch-отправку и дневной лимит клиента

**Цель:** не отправлять больше разрешённого объёма и подготовить контроль нагрузки.

Задачи:

- Использовать существующий `ClientProfile.DailySendLimit`.
- Считать дневной лимит по фактически успешным или поставленным в отправку событиям за текущие сутки UTC/MVP-день.
- Для MVP явно зафиксировать выбранную трактовку дня:
  - UTC-день проще для кода;
  - локальный день пользователя можно перенести дальше.
- Если accepted recipients больше доступного лимита:
  - отправить только доступный объём;
  - остальные пометить `Paused` или оставить pending с batch cursor;
  - статус рассылки показать как `paused` / «Приостановлено»;
  - в summary отразить число `PausedByLimit`.
- Добавить возможность продолжить отправку после смены дня или изменения лимита.
- Добавить admin UI/API для изменения `ClientProfile.DailySendLimit` в рамках Sprint 6:
  - форма или endpoint в admin-зоне;
  - проверка, что значение не отрицательное и не превышает разумный технический максимум MVP;
  - audit event изменения лимита.
- Изменение лимита не должно переписывать уже созданные `SendEvent`, но должно влиять на последующие запуски/resume.

Acceptance criteria:

- Клиент с лимитом 1000 не может отправить больше 1000 писем за выбранный день.
- Если лимит исчерпан, пользователь видит понятный статус, а письма сверх лимита не уходят даже через повторный refresh.
- Администратор может изменить дневной лимит клиента в Sprint 6 без ручного изменения БД.
- Изменение лимита администратором не ломает уже созданные `SendEvent`.

### T6-09. Добавить пользовательский UI запуска и прогресса отправки

**Цель:** заменить заглушку «Отправка — Sprint 6» на рабочий пользовательский шаг.

Задачи:

- После статуса `approved` / «Одобрено» показывать кнопку «Запустить отправку».
- Добавить endpoints, например:
  - `GET /mailings/{id}/send`;
  - `POST /mailings/{id}/send/start`;
  - `GET /mailings/{id}/send/status` или обновляемый блок на странице рассылки.
- На странице отправки показывать:
  - статус рассылки;
  - принято к отправке;
  - отправлено;
  - ошибки;
  - отписано / исключено по global suppression;
  - приостановлено по лимиту;
  - ссылку назад к рассылке.
- Для `failed` показывать безопасное сообщение без внутренних provider details.
- Для dev/admin режима можно показать provider message id и ошибки подробнее.
- Страница статуса должна читать summary из application service/repository, а не пересчитывать из HTML.

Acceptance criteria:

- Пользователь может пройти от «Одобрено» до запуска отправки без ручного ввода URL.
- Страница отправки обновляется без повторного запуска отправки.
- Нажатие back/refresh не создаёт дублей.
- Пользователь видит показатель «отписано»/«исключено», если global suppression сработал перед отправкой.

### T6-10. Добавить audit/logging отправки и изменения лимита

**Цель:** фиксировать критичные действия отправки и админского управления лимитами.

Задачи:

- Логировать события:
  - `mailing_send_requested`;
  - `mailing_send_started`;
  - `mailing_send_completed`;
  - `mailing_send_failed`;
  - `mailing_send_paused_by_limit`;
  - `email_provider_send_failed`, если нужно на уровне recipient;
  - `client_daily_send_limit_changed`.
- В audit context хранить ID рассылки и агрегированные числа, но не полный список адресов.
- Не писать тело письма в audit.
- Не писать полный список recipient emails в audit.
- Для изменения лимита хранить `clientProfileId`, старое значение, новое значение, admin user id.

Acceptance criteria:

- По audit log можно понять, кто и когда запустил отправку.
- По audit log можно понять, кто изменил дневной лимит клиента.
- Ошибки provider видны разработчику/администратору без раскрытия лишних персональных данных клиенту.

### T6-11. Добавить статистику отправки в карточку рассылки

**Цель:** пользователь должен видеть итог после отправки без перехода в техническую админку.

Задачи:

- В карточке рассылки и/или на странице `/send` показывать summary:
  - принято к отправке;
  - отправлено;
  - ошибка;
  - отписано / исключено перед отправкой по global suppression;
  - приостановлено по дневному лимиту.
- Для статуса `sent` / «Отправлено» показывать итоговую карточку.
- Для статуса `failed` / «Ошибка отправки» показывать нейтральный текст и возможность повторить только failed/pending recipients, если retry входит в Sprint 6.
- Для статуса `paused` / «Приостановлено» объяснить причину: «Достигнут дневной лимит отправки».
- Число «отписано» должно строиться из `SendEvent.Status = Skipped` + `Reason = GlobalSuppression` или эквивалентной модели, а не из текущего количества recipients.

Acceptance criteria:

- После fake-отправки пользователь видит итоговые числа.
- Итоговые числа строятся из `SendEvent`, а не из текущего количества recipients.
- Summary различает ошибку provider и исключение по global suppression.

### T6-12. Обновить dev/test данные и README

**Цель:** дать разработчику понятный способ проверить Sprint 6 вручную.

Задачи:

- Обновить README текущего среза:
  - добавить `/mailings/{id}/send`;
  - описать fake email provider;
  - описать Hangfire dev flow;
  - описать выбранный Hangfire storage;
  - описать тестовые адреса для success/fail.
- При необходимости обновить dev seed:
  - пользователь с подтверждённым email;
  - одобренная рассылка;
  - несколько accepted recipients, включая адрес для fake-fail сценария;
  - клиент с небольшим `DailySendLimit` для ручной проверки паузы.
- Если добавлен Hangfire dashboard, указать dev-only URL и ограничение доступа.
- Описать, как проверить, что после рестарта dev-приложения статистика отправки сохраняется.

Acceptance criteria:

- Новый разработчик может руками пройти Sprint 6 flow по README.
- Dev-only элементы явно помечены как dev-only.
- README не создаёт впечатление, что fake provider отправляет реальные письма.

## 5. Unit-тесты

Добавить или обновить unit-тесты:

- `MailingStatus` / status transitions:
  - отправка разрешена только из `approved`;
  - `rejected` не отправляется;
  - `paid` без проверки не отправляется;
  - переходы `sending/sent/failed/paused` валидируются техническим статусом, а не русской строкой.
- `IEmailProviderAdapter` / `FakeEmailProviderAdapter`:
  - success result;
  - failed result;
  - стабильный provider message id.
- Построение email message:
  - subject/body/sender;
  - plain text;
  - индивидуальная unsubscribe link;
  - service identifier.
- `MailingSendService`:
  - нельзя отправлять без оплаты;
  - нельзя отправлять без approve;
  - rejected не отправляется;
  - accepted recipients попадают в pending/send events;
  - global suppression повторно исключается перед отправкой;
  - suppressed recipient попадает в summary как «отписано/исключено».
- Дневной лимит:
  - лимит применяется;
  - сверхлимитные recipients не отправляются;
  - статус становится `paused`;
  - изменение лимита влияет на последующий resume.
- Batch:
  - отправка идёт порциями выбранного размера;
  - batch не превышает доступный дневной лимит.
- Идемпотентность:
  - повторный старт не создаёт дубли events;
  - повторный job не отправляет уже успешные recipients;
  - повторный refresh страницы не запускает отправку.

## 6. Интеграционные тесты

Добавить интеграционные сценарии:

- `approved` → `sending` → `sent`.
- `approved` → `sending` → `failed`, если fake provider возвращает ошибку.
- `approved` → `sending` → `paused`, если дневной лимит исчерпан.
- Создание `SendEvent` для каждого accepted recipient.
- Fake provider accepted сохраняет provider message id.
- Fake provider failed сохраняет ошибку без падения всей рассылки, если есть частичные успехи.
- Global suppression перед отправкой создаёт skipped/suppressed event и отражается в summary как «отписано».
- Проверка дневного лимита клиента.
- Admin меняет `DailySendLimit`, после чего resume учитывает новый лимит.
- Повторный POST `/send/start` не дублирует отправку.
- Страница `/send` показывает итоговую статистику.
- После рестарта dev/test приложения с PostgreSQL `SendEvent` и summary сохраняются.
- EF migration для `send_events` применяется в integration test окружении.

## 7. Ручные тесты

Проверить вручную:

1. Создать рассылку, загрузить CSV, подтвердить базу, сохранить письмо.
2. Пройти fake-оплату.
3. Пройти проверку и получить «Одобрено».
4. Открыть страницу отправки.
5. Запустить fake-отправку на несколько адресов.
6. Увидеть прогресс и итоговую статистику.
7. Обновить страницу и убедиться, что отправка не дублируется.
8. Добавить адрес с fake-fail паттерном и убедиться, что ошибка отображается в summary.
9. Добавить адрес из global suppression и убедиться, что он отражается как «отписано/исключено», а не как provider error.
10. Проверить дневной лимит: рассылка больше лимита частично отправляется или приостанавливается.
11. Изменить daily limit через admin UI/API и продолжить отправку.
12. Перезапустить dev-приложение с PostgreSQL и убедиться, что события отправки и summary сохранились.
13. Проверить, что отклонённая или не проверенная рассылка не запускается.

## 8. Не входит в Sprint 6

- Реальный email provider.
- SMTP/API credentials.
- DKIM/SPF/DMARC настройка доменов.
- Webhooks доставки, bounce, complaint.
- Публичная unsubscribe-страница и полноценная глобальная отписка — это Sprint 7.
- HTML-редактор письма.
- LLM-анализ текста.
- Полноценная RBAC-модель админки.
- Production dashboard для очередей.

Важно: публичная unsubscribe-страница не входит в Sprint 6, но повторный учёт уже существующего `GlobalSuppression` перед отправкой и статистика «отписано/исключено» входят в Sprint 6.

## 9. Definition of Done

Sprint 6 считается закрытым, если:

- пользователь может запустить отправку только для оплаченной и одобренной рассылки;
- отправка выполняется через Hangfire job, а не внутри HTTP request;
- отправка выполняется через fake provider;
- реализован `IEmailProviderAdapter`;
- реализована batch-отправка;
- для каждого recipient создаётся устойчивый `SendEvent` в EF/PostgreSQL;
- успешные fake-отправки получают provider message id;
- ошибки fake provider отображаются в summary;
- global suppression перед отправкой отражается в summary как «отписано/исключено»;
- дневной лимит клиента применяется;
- дневной лимит можно изменить через admin UI/API;
- повторный запуск не создаёт дубли отправок;
- технические переходы отправки используют `MailingStatus`/status-code, а не русские строки;
- UI показывает прогресс и итог;
- после рестарта dev-приложения с PostgreSQL события отправки и итоговая статистика сохраняются;
- unit и integration тесты покрывают основной happy path, ошибки provider, лимит, suppression, batch, persistence и идемпотентность;
- README обновлён под новый dev-flow.

## 10. Документы и разделы для синхронизации

После реализации Sprint 6 нужно синхронизировать:

- `README.md` — текущий dev-срез, новые endpoints, Hangfire flow и fake provider;
- `docs/platform_tz.md` — разделы про очередь, email provider adapter, Hangfire, SendEvent, batch-отправку, persistence и лимиты;
- `docs/sprints.md` — только если по итогам реализации изменится утверждённый состав Sprint 6;
- `docs/specification.md` — только если изменится продуктовая логика лимитов, видимой статистики или правил запуска отправки.

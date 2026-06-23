# SimpleMail / Письмолёт — архитектура .NET MVP

Версия: 0.1  
Дата: 2026-06-15  
Статус: рабочий архитектурный документ

## 1. Назначение документа

Документ описывает целевую архитектуру MVP web-сервиса SimpleMail / «Письмолёт» на стеке `.NET + EF Core + Blazor + PostgreSQL`.

Документ подготовлен на основании:

- `docs/specification.md`;
- `docs/platform_tz.md` из ветки `main`.

Важно: в текущем `docs/platform_tz.md` на момент подготовки этого документа выбран стек `Python + Django + Celery`. Настоящий документ фиксирует альтернативную / целевую архитектуру под `.NET`, поэтому `docs/platform_tz.md` требует последующей синхронизации, если стек `.NET` принят как основной.

## 2. Архитектурные принципы MVP

1. Клиент видит одну простую сущность — «Рассылка».
2. Внутренние сущности вроде импорта, получателей, кампании, событий отправки, платежей и модерации скрыты от пользователя.
3. MVP не является сложной email-маркетинговой платформой.
4. Пользователь не настраивает DNS, SPF, DKIM, DMARC, SMTP и домен отправки.
5. Отправка идёт с домена сервиса через внешний email-провайдер по API.
6. Ответы получателей принимает сервис и пересылает клиенту.
7. Отписка глобальная для всего сервиса.
8. Денежный баланс клиента в MVP не хранится.
9. Основной платёжный сценарий — оплата конкретной рассылки перед отправкой.
10. Risk-проверка в MVP выполняется формальными правилами без LLM-модерации.
11. Все критичные действия логируются.
12. Система проектируется так, чтобы её могли безопасно развивать LLM-агенты при наличии тестов, ADR и human review для критичных сценариев.

## 3. Целевой стек

### Backend

- .NET 9 или актуальная LTS-версия .NET на момент старта разработки.
- ASP.NET Core.
- ASP.NET Core Identity для регистрации, входа, подтверждения email и ролей.
- Minimal API или Controllers. Для MVP предпочтительны Controllers в отдельных feature-модулях, так как они проще для LLM-агентов и code review.
- FluentValidation для валидации входных payload.
- MediatR опционально. Для MVP можно начать без него, чтобы не усложнять код.

### Frontend

- Blazor Server для MVP.
- Razor Components.
- Минимум JavaScript.
- Без React / Next.js / отдельной SPA в MVP.

Обоснование Blazor Server для MVP:

- единый язык и стек для backend и frontend;
- проще поддерживать LLM-агентами;
- быстрее собрать личный кабинет, админские экраны и пошаговый flow;
- не нужен отдельный публичный API для всего интерфейса на старте.

### База данных

- PostgreSQL.
- EF Core.
- Npgsql provider.
- Миграции через EF Core migrations.
- UUID / Guid для публичных идентификаторов.
- `bigint` identity допустим для внутренних технических ключей, но наружу лучше отдавать `public_id`.

### Очереди и фоновые задачи

Рекомендуемый вариант для .NET MVP:

- Hangfire + PostgreSQL storage.

Альтернатива:

- RabbitMQ + hosted workers, если потребуется более строгая очередь.

Для MVP предпочтительнее Hangfire, потому что:

- меньше инфраструктуры;
- есть dashboard для внутренних операций;
- задачи можно делать идемпотентными;
- достаточно для импорта, отправки, webhooks, пересылки ответов и cleanup.

Очереди / категории задач:

- `import` — обработка CSV/XLSX;
- `risk` — формальная risk-проверка;
- `send` — постановка и отправка писем;
- `webhook` — обработка событий ESP;
- `reply` — обработка и пересылка входящих ответов;
- `billing` — проверка статусов платежей;
- `cleanup` — удаление временных тел писем, файлов и вложений.

Правило: в фоновые задачи передаются ID сущностей, а не тела писем, файлы или списки адресов.

### Email

- Внешний ESP через API.
- Основной кандидат: Postmark.
- Резервные варианты: Mailgun, Amazon SES, Unisender.
- В коде используется интерфейс `IEmailProviderAdapter`, чтобы не привязываться к одному провайдеру.
- Собственный SMTP-сервер не входит в MVP.

### Импорт файлов

- CSV обязателен.
- XLSX желательно поддержать в MVP, если не усложняет сроки.
- Для CSV: CsvHelper.
- Для XLSX: ClosedXML или EPPlus с проверкой лицензии перед использованием.
- Файлы импорта хранятся временно.
- После обработки и истечения срока хранения файл удаляется cleanup-задачей.

### Платежи

- Интеграция с платёжным агентом / российским эквайрингом.
- В MVP хранится не баланс, а попытки оплаты и факт оплаты конкретной рассылки.
- Возможное развитие: пакеты писем-кредитов, но не рублёвый кошелёк клиента.

### Админка

Варианты:

1. Blazor Admin внутри основного приложения.
2. Отдельная area `/admin` с Razor / Blazor components.

Для MVP рекомендуется Blazor Admin внутри основного приложения с ролью `Admin`.

Админка должна покрывать:

- клиентов;
- рассылки;
- импорты;
- очередь ручной проверки;
- жалобы;
- отписки;
- ошибки доставки;
- лимиты;
- платежи;
- журналы событий.

### Инфраструктура

- Docker Compose на старте.
- Контейнеры:
  - `web` — ASP.NET Core / Blazor;
  - `postgres` — PostgreSQL;
  - опционально `maildev` / fake ESP для локальной разработки;
  - опционально `reverse-proxy` — Caddy или Nginx.
- Локальный запуск должен быть возможен одной командой.
- Kubernetes не входит в MVP.

## 4. Логическая схема компонентов

```text
Browser
  ↓
Blazor Server / ASP.NET Core Web App
  ↓
Application Services
  ↓
EF Core
  ↓
PostgreSQL

ASP.NET Core Web App
  ↓
Hangfire Jobs
  ↓
IEmailProviderAdapter
  ↓
External ESP API

External ESP Webhooks
  ↓
ASP.NET Core Webhook Endpoints
  ↓
PostgreSQL + Hangfire Jobs
```

## 5. Рекомендуемая структура solution

```text
src/
  Pismolet.Web/
    Components/
    Pages/
    Admin/
    Endpoints/
    Program.cs
    appsettings.json

  Pismolet.Application/
    Auth/
    Clients/
    Mailings/
    Imports/
    Recipients/
    Sending/
    Inbound/
    Suppression/
    Moderation/
    Billing/
    Audit/

  Pismolet.Domain/
    Entities/
    Enums/
    ValueObjects/
    Events/

  Pismolet.Infrastructure/
    Persistence/
    Email/
    Payments/
    FileStorage/
    Jobs/
    Security/

  Pismolet.Tests/
    Unit/
    Integration/
    E2E/
```

Для LLM-агентов важно держать feature-код рядом по смыслу, не превращая MVP в избыточную Clean Architecture. Разделение на Domain / Application / Infrastructure / Web достаточно, но не нужно добавлять сложные CQRS-паттерны без необходимости.

## 6. Основные доменные модули

### Accounts

Отвечает за:

- регистрацию;
- вход;
- выход;
- подтверждение email;
- сброс пароля;
- роли `Client`, `Admin`.

### Clients

Отвечает за:

- профиль клиента;
- статус клиента;
- дневные и общие лимиты;
- флаги премодерации;
- блокировки.

### Mailings

Внешний пользовательский flow одной рассылки:

- черновик;
- загрузка адресов;
- текст письма;
- декларации;
- расчёт;
- оплата;
- проверки;
- отправка;
- простой результат.

### Imports

Отвечает за:

- загрузку CSV/XLSX;
- нормализацию email;
- поиск дублей;
- проверку синтаксиса;
- проверку глобальной отписки;
- статистику импорта;
- создание разрешённых получателей.

### Recipients

Отвечает за:

- адресатов конкретного клиента и рассылки;
- статусы адресов;
- bounce в рамках клиента;
- связь с событиями отправки.

### Sending

Отвечает за:

- очередь отправки;
- лимиты;
- генерацию служебных заголовков;
- вызов `IEmailProviderAdapter`;
- фиксацию `SendEvent`.

### Inbound

Отвечает за:

- inbound replies;
- bounce webhooks;
- complaints;
- пересылку ответа клиенту;
- временное хранение тела ответа и вложений.

### Suppression

Отвечает за:

- глобальные отписки;
- блок-листы;
- исключение адресов при импорте и перед отправкой;
- защиту unsubscribe-ссылок от перебора.

### Moderation

Отвечает за:

- формальные risk-правила без LLM;
- risk-score;
- ручную премодерацию;
- решение: пропустить, предупредить, отправить на проверку, заблокировать.

### Billing

Отвечает за:

- расчёт стоимости;
- создание оплаты конкретной рассылки;
- обработку callback от платёжного агента;
- перевод рассылки в paid;
- идемпотентность платежей.

### Audit

Отвечает за:

- журналирование ключевых действий;
- хранение IP, user-agent, пользователя, времени, версии декларации;
- события администраторов.

## 7. Основные сущности БД

Ниже приведён минимальный набор таблиц. Названия можно адаптировать под EF Core naming convention, но в БД предпочтителен `snake_case`.

### users

Пользователь системы. Реализуется через ASP.NET Core Identity.

Ключевые поля:

- `id`;
- `public_id`;
- `email`;
- `email_confirmed`;
- `password_hash`;
- `created_at`;
- `updated_at`;
- `last_login_at`.

### client_profiles

Профиль клиента.

Ключевые поля:

- `id`;
- `public_id`;
- `user_id`;
- `display_name`;
- `contact_email`;
- `status` — active, blocked, pending_review;
- `daily_send_limit`;
- `total_send_limit`;
- `premoderation_required`;
- `risk_level`;
- `created_at`;
- `updated_at`.

### mailings

Главная пользовательская сущность — рассылка.

Ключевые поля:

- `id`;
- `public_id`;
- `client_profile_id`;
- `status`;
- `subject`;
- `sender_display_name`;
- `body_text`;
- `body_html`;
- `message_type` — informational, advertising, service, unknown;
- `accepted_recipient_count`;
- `excluded_recipient_count`;
- `price_per_email`;
- `total_price`;
- `paid_at`;
- `created_at`;
- `updated_at`;
- `sent_at`.

Статусы:

```text
draft
uploaded
content_ready
priced
payment_pending
paid
pending_checks
review_required
approved
sending
sent
cancelled
blocked
failed
paused
```

### import_batches

Партия импорта адресов.

Ключевые поля:

- `id`;
- `public_id`;
- `mailing_id`;
- `original_file_name`;
- `storage_path`;
- `status`;
- `total_rows`;
- `valid_count`;
- `duplicate_count`;
- `invalid_count`;
- `globally_suppressed_count`;
- `blocked_count`;
- `created_at`;
- `processed_at`.

Статусы:

```text
uploaded
processing
processed
waiting_declaration
confirmed
failed
blocked
```

### recipients

Адресаты, разрешённые или исключённые в рамках рассылки.

Ключевые поля:

- `id`;
- `public_id`;
- `mailing_id`;
- `import_batch_id`;
- `email_normalized`;
- `email_original`;
- `name`;
- `status` — accepted, duplicate, invalid, suppressed, blocked, bounced;
- `exclude_reason`;
- `created_at`.

Примечание: для новых клиентов в MVP может быть ограничение на загрузку ФИО и персонализацию. В таком режиме `name` не заполняется, пока клиент не получит достаточную репутацию.

### base_declarations

Подтверждение законности базы.

Ключевые поля:

- `id`;
- `mailing_id`;
- `import_batch_id`;
- `client_profile_id`;
- `source_type`;
- `source_comment`;
- `declaration_text_version`;
- `confirmed_at`;
- `confirmed_by_user_id`;
- `ip_address`;
- `user_agent`.

### ad_consent_confirmations

Отдельное подтверждение согласия на рекламную рассылку.

Ключевые поля:

- `id`;
- `mailing_id`;
- `confirmation_text_version`;
- `confirmed_at`;
- `confirmed_by_user_id`;
- `ip_address`;
- `user_agent`.

### global_suppressions

Глобальная отписка и suppression list всего сервиса.

Ключевые поля:

- `id`;
- `email_normalized`;
- `reason` — unsubscribe, complaint, admin_block, hard_bounce_policy;
- `source`;
- `created_at`;
- `created_by_user_id`;
- `metadata_json`.

Ограничение: уникальный индекс по `email_normalized`.

### send_events

События отправки и доставки.

Ключевые поля:

- `id`;
- `mailing_id`;
- `recipient_id`;
- `provider_message_id`;
- `event_type` — queued, accepted, delivered, soft_bounce, hard_bounce, complaint, rejected;
- `event_at`;
- `provider_payload_json`;
- `created_at`.

### reply_events

Ответы получателей.

Ключевые поля:

- `id`;
- `mailing_id`;
- `recipient_id`;
- `client_profile_id`;
- `provider_message_id`;
- `from_email`;
- `subject`;
- `body_storage_path`;
- `attachments_storage_path`;
- `received_at`;
- `forwarded_at`;
- `delete_body_after`;
- `created_at`.

### payments

Оплата конкретной рассылки.

Ключевые поля:

- `id`;
- `public_id`;
- `mailing_id`;
- `client_profile_id`;
- `amount`;
- `currency` — RUB;
- `status` — pending, succeeded, failed, cancelled, refunded;
- `provider`;
- `provider_payment_id`;
- `payment_url`;
- `idempotency_key`;
- `created_at`;
- `paid_at`;
- `updated_at`.

### moderation_reviews

Результаты risk-проверки и ручной проверки.

Ключевые поля:

- `id`;
- `mailing_id`;
- `status` — not_required, pending, approved, rejected, blocked;
- `risk_score`;
- `risk_categories_json`;
- `rules_triggered_json`;
- `recommendation`;
- `reviewer_user_id`;
- `review_comment`;
- `created_at`;
- `reviewed_at`.

### audit_logs

Журнал значимых действий.

Ключевые поля:

- `id`;
- `actor_user_id`;
- `client_profile_id`;
- `action`;
- `entity_type`;
- `entity_id`;
- `ip_address`;
- `user_agent`;
- `metadata_json`;
- `created_at`.

### service_settings

Настройки сервиса.

Ключевые поля:

- `id`;
- `key`;
- `value_json`;
- `updated_at`;
- `updated_by_user_id`.

Примеры настроек:

- цена письма;
- дневной лимит по умолчанию;
- общий лимит по умолчанию;
- включать ли премодерацию новым клиентам;
- срок хранения входящих ответов.

## 8. Основные индексы и ограничения

1. `users.email` — unique.
2. `client_profiles.user_id` — unique.
3. `mailings.public_id` — unique.
4. `mailings.client_profile_id, status`.
5. `import_batches.mailing_id`.
6. `recipients.mailing_id, email_normalized` — unique для защиты от дублей внутри рассылки.
7. `recipients.email_normalized`.
8. `global_suppressions.email_normalized` — unique.
9. `send_events.mailing_id, recipient_id`.
10. `send_events.provider_message_id`.
11. `payments.provider_payment_id` — unique nullable.
12. `payments.idempotency_key` — unique.
13. `audit_logs.created_at`.
14. `audit_logs.entity_type, entity_id`.

## 9. API endpoints и payload

Даже если основной UI реализован на Blazor Server, внутренние endpoints нужны для webhooks, фоновых действий, загрузки файлов и возможной будущей интеграции.

Формат ошибок:

```json
{
  "error": {
    "code": "validation_error",
    "message": "Проверьте поля формы",
    "details": [
      { "field": "email", "message": "Некорректный email" }
    ]
  }
}
```

### Auth

#### POST /api/auth/register

Request:

```json
{
  "email": "user@example.com",
  "password": "string",
  "displayName": "ИП Иванов"
}
```

Response:

```json
{
  "userId": "uuid",
  "emailConfirmationRequired": true
}
```

#### POST /api/auth/login

Request:

```json
{
  "email": "user@example.com",
  "password": "string"
}
```

Response:

```json
{
  "success": true,
  "userId": "uuid"
}
```

#### POST /api/auth/logout

Request: empty.

Response:

```json
{ "success": true }
```

#### POST /api/auth/confirm-email

Request:

```json
{
  "userId": "uuid",
  "token": "string"
}
```

Response:

```json
{ "success": true }
```

### Client profile

#### GET /api/client/profile

Response:

```json
{
  "clientId": "uuid",
  "displayName": "ИП Иванов",
  "contactEmail": "user@example.com",
  "status": "active",
  "dailySendLimit": 1000,
  "premoderationRequired": false
}
```

#### PATCH /api/client/profile

Request:

```json
{
  "displayName": "ИП Иванов",
  "contactEmail": "user@example.com"
}
```

Response:

```json
{ "success": true }
```

### Mailings

#### POST /api/mailings

Создать черновик рассылки.

Request:

```json
{
  "title": "Рассылка участникам мероприятия"
}
```

Response:

```json
{
  "mailingId": "uuid",
  "status": "draft"
}
```

#### GET /api/mailings

Query:

```text
status=sent&page=1&pageSize=20
```

Response:

```json
{
  "items": [
    {
      "mailingId": "uuid",
      "title": "Рассылка участникам мероприятия",
      "status": "sent",
      "acceptedRecipientCount": 450,
      "totalPrice": 90.00,
      "createdAt": "2026-06-15T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 1
}
```

#### GET /api/mailings/{mailingId}

Response:

```json
{
  "mailingId": "uuid",
  "status": "priced",
  "subject": "Важная информация",
  "senderDisplayName": "Письмолёт",
  "messageType": "informational",
  "importStats": {
    "totalRows": 500,
    "validCount": 450,
    "duplicateCount": 20,
    "invalidCount": 10,
    "globallySuppressedCount": 20
  },
  "pricing": {
    "acceptedRecipientCount": 450,
    "pricePerEmail": 0.20,
    "totalPrice": 90.00,
    "currency": "RUB"
  }
}
```

#### PATCH /api/mailings/{mailingId}/content

Request:

```json
{
  "senderDisplayName": "ИП Иванов",
  "subject": "Важная информация для клиентов",
  "bodyText": "Здравствуйте! ...",
  "messageType": "informational"
}
```

Response:

```json
{
  "mailingId": "uuid",
  "status": "content_ready"
}
```

#### POST /api/mailings/{mailingId}/cancel

Request:

```json
{
  "reason": "Передумал отправлять"
}
```

Response:

```json
{
  "mailingId": "uuid",
  "status": "cancelled"
}
```

### Imports

#### POST /api/mailings/{mailingId}/imports

Content-Type: `multipart/form-data`.

Payload:

```text
file: CSV/XLSX
```

Response:

```json
{
  "importBatchId": "uuid",
  "status": "uploaded"
}
```

После загрузки запускается background job обработки импорта.

#### GET /api/mailings/{mailingId}/imports/{importBatchId}

Response:

```json
{
  "importBatchId": "uuid",
  "status": "processed",
  "stats": {
    "totalRows": 500,
    "validCount": 450,
    "duplicateCount": 20,
    "invalidCount": 10,
    "globallySuppressedCount": 20,
    "blockedCount": 0
  }
}
```

#### GET /api/mailings/{mailingId}/recipients/excluded

Query:

```text
reason=invalid&page=1&pageSize=50
```

Response:

```json
{
  "items": [
    {
      "emailMasked": "iv***@example.com",
      "reason": "invalid"
    }
  ],
  "page": 1,
  "pageSize": 50,
  "total": 1
}
```

### Declarations

#### POST /api/mailings/{mailingId}/base-declaration

Request:

```json
{
  "sourceType": "customers",
  "sourceComment": "Клиенты, ранее покупавшие услугу",
  "declarationAccepted": true,
  "declarationTextVersion": "2026-06-15"
}
```

Response:

```json
{
  "success": true,
  "mailingId": "uuid",
  "status": "confirmed"
}
```

#### POST /api/mailings/{mailingId}/ad-consent-confirmation

Request:

```json
{
  "adConsentAccepted": true,
  "confirmationTextVersion": "2026-06-15"
}
```

Response:

```json
{ "success": true }
```

### Pricing and payment

#### POST /api/mailings/{mailingId}/price

Request: empty.

Response:

```json
{
  "mailingId": "uuid",
  "status": "priced",
  "acceptedRecipientCount": 450,
  "pricePerEmail": 0.20,
  "totalPrice": 90.00,
  "currency": "RUB"
}
```

#### POST /api/mailings/{mailingId}/payments

Request:

```json
{
  "returnUrl": "https://pismolet.example/mailings/uuid/payment-result"
}
```

Response:

```json
{
  "paymentId": "uuid",
  "status": "pending",
  "amount": 90.00,
  "currency": "RUB",
  "paymentUrl": "https://payment-provider.example/pay/123"
}
```

#### GET /api/payments/{paymentId}

Response:

```json
{
  "paymentId": "uuid",
  "mailingId": "uuid",
  "status": "succeeded",
  "amount": 90.00,
  "currency": "RUB",
  "paidAt": "2026-06-15T10:10:00Z"
}
```

#### POST /api/webhooks/payments/{provider}

Webhook платёжного агента.

Request: provider-specific JSON.

Response:

```json
{ "received": true }
```

Требования:

- проверять подпись webhook;
- обрабатывать идемпотентно;
- не доверять сумме только из frontend;
- после успешной оплаты переводить рассылку в `paid` и запускать проверки.

### Sending

#### POST /api/mailings/{mailingId}/submit

Запуск рассылки после оплаты и проверок.

Request:

```json
{
  "confirmSubmit": true
}
```

Response:

```json
{
  "mailingId": "uuid",
  "status": "pending_checks"
}
```

#### POST /api/mailings/{mailingId}/send-test

Request:

```json
{
  "email": "user@example.com"
}
```

Response:

```json
{
  "success": true,
  "message": "Тестовое письмо поставлено в очередь"
}
```

#### GET /api/mailings/{mailingId}/stats

Response:

```json
{
  "mailingId": "uuid",
  "status": "sent",
  "accepted": 450,
  "delivered": 430,
  "softBounced": 5,
  "hardBounced": 10,
  "complaints": 1,
  "unsubscribed": 4,
  "replies": 12
}
```

### Unsubscribe

#### GET /u/{token}

Публичная страница подтверждения отписки.

Response: HTML page.

#### POST /api/unsubscribe

Request:

```json
{
  "token": "signed-token",
  "confirm": true
}
```

Response:

```json
{
  "success": true,
  "message": "Адрес отписан от всех рассылок Письмолёта"
}
```

Требования:

- token должен быть подписан;
- нельзя раскрывать лишние сведения о клиенте;
- повторная отписка должна быть безопасной и идемпотентной.

### ESP webhooks

#### POST /api/webhooks/email/{provider}/delivery

Request: provider-specific JSON.

Response:

```json
{ "received": true }
```

#### POST /api/webhooks/email/{provider}/bounce

Request: provider-specific JSON.

Response:

```json
{ "received": true }
```

#### POST /api/webhooks/email/{provider}/complaint

Request: provider-specific JSON.

Response:

```json
{ "received": true }
```

#### POST /api/webhooks/email/{provider}/inbound

Request: provider-specific JSON / MIME payload depending on provider.

Response:

```json
{ "received": true }
```

Требования ко всем ESP webhooks:

- проверять подпись / basic auth / token провайдера;
- сохранять provider payload для аудита в ограниченном виде;
- обрабатывать идемпотентно;
- не выполнять тяжёлую обработку синхронно;
- ставить job в нужную очередь.

### Admin

Все admin endpoints требуют роль `Admin`.

#### GET /api/admin/clients

Response:

```json
{
  "items": [
    {
      "clientId": "uuid",
      "email": "user@example.com",
      "status": "active",
      "dailySendLimit": 1000,
      "premoderationRequired": false,
      "riskLevel": "normal"
    }
  ]
}
```

#### PATCH /api/admin/clients/{clientId}/limits

Request:

```json
{
  "dailySendLimit": 1000,
  "totalSendLimit": 10000
}
```

Response:

```json
{ "success": true }
```

#### POST /api/admin/clients/{clientId}/block

Request:

```json
{
  "reason": "Подозрение на запрещённую базу"
}
```

Response:

```json
{ "success": true }
```

#### GET /api/admin/moderation/reviews

Query:

```text
status=pending&page=1&pageSize=20
```

Response:

```json
{
  "items": [
    {
      "reviewId": "uuid",
      "mailingId": "uuid",
      "clientId": "uuid",
      "riskScore": 75,
      "categories": ["spam", "suspicious_links"],
      "createdAt": "2026-06-15T10:00:00Z"
    }
  ]
}
```

#### POST /api/admin/moderation/reviews/{reviewId}/approve

Request:

```json
{
  "comment": "Проверено вручную"
}
```

Response:

```json
{ "success": true }
```

#### POST /api/admin/moderation/reviews/{reviewId}/reject

Request:

```json
{
  "comment": "Нельзя отправлять по указанной базе"
}
```

Response:

```json
{ "success": true }
```

#### GET /api/admin/audit-logs

Query:

```text
entityType=mailing&entityId=123&page=1&pageSize=50
```

Response:

```json
{
  "items": [
    {
      "action": "mailing.submitted",
      "actorEmail": "user@example.com",
      "createdAt": "2026-06-15T10:00:00Z",
      "metadata": {}
    }
  ]
}
```

## 10. Пользовательский workflow

### 10.1 Регистрация и подготовка

1. Пользователь открывает сервис.
2. Регистрируется по email и паролю.
3. Подтверждает email.
4. Попадает в личный кабинет.
5. Видит простой CTA: «Создать рассылку».

### 10.2 Создание рассылки

1. Пользователь создаёт черновик рассылки.
2. Загружает CSV/XLSX с колонкой `email`.
3. Система создаёт `ImportBatch`.
4. Background job обрабатывает файл:
   - читает строки;
   - нормализует email;
   - удаляет дубли;
   - проверяет синтаксис;
   - проверяет глобальную отписку;
   - проверяет внутренние блок-листы;
   - считает статистику.
5. Пользователь видит результат:
   - сколько адресов принято;
   - сколько дублей;
   - сколько невалидных;
   - сколько исключено из-за глобальной отписки;
   - сколько будет стоить отправка.

### 10.3 Подтверждение базы

1. Пользователь выбирает источник базы.
2. Если выбран «другое», вводит пояснение.
3. Пользователь вручную ставит галочку декларации.
4. Система логирует:
   - дату;
   - IP;
   - user-agent;
   - пользователя;
   - версию текста;
   - import / mailing.
5. Без подтверждения отправка невозможна.

### 10.4 Создание письма

1. Пользователь задаёт отображаемое имя отправителя.
2. Указывает тему.
3. Пишет простой текст письма.
4. Выбирает тип письма:
   - информационное;
   - рекламное;
   - сервисное;
   - неизвестное.
5. Если письмо рекламное, подтверждает наличие согласия на рекламу.
6. Система автоматически добавляет:
   - ссылку глобальной отписки;
   - служебный блок «почему вы получили это письмо»;
   - идентификатор рассылки;
   - технические заголовки;
   - plain text версию.

### 10.5 Расчёт и оплата

1. Система считает только адреса, принятые к отправке.
2. Исключённые адреса не оплачиваются.
3. Пользователь видит стоимость.
4. Пользователь переходит на оплату.
5. Платёжный агент возвращает webhook.
6. Система проверяет подпись и сумму.
7. При успешной оплате создаётся `Payment` со статусом `succeeded`.
8. Рассылка переходит в `paid`.

### 10.6 Проверки и модерация

1. После оплаты запускается risk-проверка.
2. Формальные правила проверяют:
   - подозрительные ссылки;
   - запрещённые темы;
   - агрессивную рекламу;
   - отсутствие понятного отправителя;
   - отсутствие причины получения письма;
   - жалобы и историю клиента.
3. Возможные решения:
   - пропустить;
   - предупредить;
   - отправить на ручную проверку;
   - заблокировать.
4. Если нужна ручная проверка, администратор видит рассылку в очереди модерации.
5. Администратор одобряет или отклоняет рассылку.

### 10.7 Отправка

1. Одобренная рассылка переходит в `approved`.
2. Система ставит отправку в очередь.
3. Job проверяет дневной лимит клиента.
4. Job повторно проверяет глобальную отписку перед отправкой.
5. Для каждого получателя вызывается `IEmailProviderAdapter`.
6. События фиксируются в `send_events`.
7. Рассылка переходит в `sending`, затем в `sent`.

### 10.8 Отписка получателя

1. Получатель нажимает ссылку отписки.
2. Открывается публичная страница `/u/{token}`.
3. Получатель подтверждает отписку.
4. Email добавляется в `global_suppressions`.
5. Адрес больше не получает письма от любых клиентов сервиса.
6. Клиенту не раскрываются лишние сведения о глобальной отписке.

### 10.9 Ответ получателя

1. Получатель отвечает на письмо.
2. ESP принимает inbound email.
3. ESP вызывает webhook `/api/webhooks/email/{provider}/inbound`.
4. Сервис определяет клиента, рассылку и адресата.
5. Создаёт `reply_event`.
6. Пересылает ответ клиенту.
7. Тело и вложения хранятся ограниченный срок.
8. Cleanup job удаляет тело и вложения, оставляя технический лог.

### 10.10 Просмотр результата

Пользователь видит простой результат рассылки:

- принято к отправке;
- доставлено;
- ошибки доставки;
- жалобы;
- отписки;
- ответы.

Сложная аналитика, сегменты, A/B-тесты и автоворонки не входят в MVP.

## 11. State machine

### Mailing

```text
draft
  → uploaded
  → content_ready
  → priced
  → payment_pending
  → paid
  → pending_checks
  → review_required
  → approved
  → sending
  → sent
```

Боковые статусы:

```text
cancelled
blocked
failed
paused
```

### ImportBatch

```text
uploaded
  → processing
  → processed
  → waiting_declaration
  → confirmed
  → failed
  → blocked
```

### Payment

```text
pending
  → succeeded
  → failed
  → cancelled
  → refunded
```

### ModerationReview

```text
not_required
pending
approved
rejected
blocked
```

## 12. Контракты адаптеров

### IEmailProviderAdapter

```csharp
public interface IEmailProviderAdapter
{
    Task<SendEmailResult> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken);
    Task<SendEmailResult> SendTestEmailAsync(EmailMessage message, CancellationToken cancellationToken);
    Task<DeliveryEvent> ParseDeliveryWebhookAsync(Stream body, IHeaderDictionary headers, CancellationToken cancellationToken);
    Task<BounceEvent> ParseBounceWebhookAsync(Stream body, IHeaderDictionary headers, CancellationToken cancellationToken);
    Task<ComplaintEvent> ParseComplaintWebhookAsync(Stream body, IHeaderDictionary headers, CancellationToken cancellationToken);
    Task<InboundReplyEvent> ParseInboundReplyAsync(Stream body, IHeaderDictionary headers, CancellationToken cancellationToken);
}
```

Provider events:

- accepted;
- delivered;
- soft_bounce;
- hard_bounce;
- complaint;
- rejected;
- inbound_reply.

### IPaymentProviderAdapter

```csharp
public interface IPaymentProviderAdapter
{
    Task<CreatePaymentResult> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentWebhookEvent> ParseWebhookAsync(Stream body, IHeaderDictionary headers, CancellationToken cancellationToken);
    Task<bool> VerifyWebhookAsync(Stream body, IHeaderDictionary headers, CancellationToken cancellationToken);
}
```

### IFileStorage

```csharp
public interface IFileStorage
{
    Task<string> SaveAsync(Stream file, string fileName, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken);
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken);
}
```

## 13. Безопасность и комплаенс

1. Все формы с юридически значимыми подтверждениями не должны иметь заранее проставленных галочек.
2. Декларации и согласия версионируются.
3. Все подтверждения логируются.
4. Webhook endpoints защищаются подписью, secret token или basic auth провайдера.
5. Unsubscribe token подписывается и имеет защиту от перебора.
6. Админские endpoints требуют роль `Admin`.
7. Клиент не должен видеть чужие рассылки, импорты, платежи или отписки.
8. В логах не хранить лишние персональные данные.
9. Тела входящих ответов и вложения хранятся ограниченный срок.
10. Купленные, спарсенные и чужие базы запрещены правилами сервиса.

## 14. Тестирование

Минимальный набор тестов:

### Unit tests

- нормализация email;
- поиск дублей;
- проверка глобальной отписки;
- расчёт стоимости;
- state machine рассылки;
- state machine импорта;
- idempotency платежей;
- idempotency webhooks;
- risk-правила;
- генерация unsubscribe token.

### Integration tests

- регистрация и подтверждение email;
- создание рассылки;
- загрузка CSV;
- обработка импорта;
- подтверждение декларации;
- расчёт цены;
- создание платежа;
- payment webhook;
- запуск отправки;
- fake email provider;
- ESP webhook delivery / bounce / complaint;
- inbound reply webhook;
- глобальная отписка.

### Manual acceptance tests

1. Клиент регистрируется и подтверждает email.
2. Клиент создаёт рассылку.
3. Клиент загружает CSV с валидными, невалидными и дублями.
4. Система показывает корректную статистику.
5. Клиент подтверждает законность базы.
6. Клиент пишет письмо.
7. Система добавляет отписку.
8. Система считает стоимость.
9. Клиент оплачивает рассылку.
10. После оплаты запускается risk-проверка.
11. Подозрительная рассылка попадает в админскую ручную проверку.
12. Администратор одобряет рассылку.
13. Рассылка отправляется через fake provider.
14. Получатель отписывается.
15. Глобально отписанный адрес исключается из следующей рассылки.
16. Ответ получателя пересылается клиенту.
17. Клиент видит простой результат.
18. Все ключевые действия видны в audit log.

## 15. Что требует синхронизации в других документах

Если стек `.NET + EF Core + Blazor + PostgreSQL` принимается как основной, нужно синхронизировать:

1. `docs/platform_tz.md`:
   - раздел 3 «Выбранный стек MVP»;
   - раздел 4 «Общая архитектура MVP»;
   - раздел 7 «Основные backend-приложения Django»;
   - раздел 13 «Очереди Celery»;
   - раздел 16 «EmailProviderAdapter» — заменить Python-контракт на C#-контракт;
   - раздел 21 «Админка» — заменить Django Admin на Blazor Admin;
   - раздел 23 «Требования к разработке LLM-агентами» — уточнить .NET-практики;
   - раздел 24 «Acceptance criteria MVP» — оставить смысл, но привязать к .NET/Docker Compose.
2. `docs/specification.md`:
   - синхронизация не обязательна, так как документ продуктовый и юридический;
   - можно добавить ссылку на этот архитектурный документ в раздел «Назначение документа» или приложение «Границы документов».

## 16. Границы MVP

Не входит в MVP:

- собственный SMTP;
- Kubernetes;
- микросервисы;
- React / Next.js SPA;
- подключение домена клиента;
- Kafka / event streaming;
- сложный визуальный редактор писем;
- CRM;
- сегментация;
- автоворонки;
- A/B-тесты;
- сложная аналитика;
- пользовательские домены отправки;
- автоматическая LLM-модерация контента;
- рублёвый кошелёк клиента.

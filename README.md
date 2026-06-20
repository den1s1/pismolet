# pismolet

MVP web-сервиса простых email-рассылок «Письмолёт».

## Development / текущий срез

Ветка `Development` содержит .NET / ASP.NET Core vertical slice приложения.

Реализовано:

- регистрация пользователя;
- вход и выход;
- подтверждение email через dev/fake mailer;
- создание профиля клиента с лимитами;
- защищённый личный кабинет;
- создание черновика рассылки;
- загрузка CSV с колонкой `email`;
- проверка адресов: валидные, дубли, невалидные, исключённые по глобальной отписке и по hard bounce у клиента;
- подтверждение базы адресов после импорта;
- редактор простого plain text письма;
- preview служебных блоков письма;
- fake-оплата MVP;
- формальная проверка перед отправкой и ручная dev/admin модерация;
- fake-отправка Sprint 6 через `FakeEmailProviderAdapter`;
- публичная unsubscribe-страница Sprint 7;
- Sprint 8 fake email webhooks: accepted/delivered/soft bounce/hard bounce/complaint/rejected/unknown;
- устойчивый EF/PostgreSQL `global_suppressions` для глобальной отписки;
- устойчивый EF/PostgreSQL `send_events`, `provider_webhook_events`, `client_suppressions`;
- production-срез очереди: Hangfire + PostgreSQL storage для Postgres provider;
- dev-only in-memory audit log.

## Структура кода

- `src/Pismolet.Domain` — доменные модели;
- `src/Pismolet.Application` — сценарии и интерфейсы;
- `src/Pismolet.Infrastructure` — persistence, dev fallback, Hangfire и fake-инфраструктура;
- `src/Pismolet.Web/Endpoints` — HTTP endpoints;
- `src/Pismolet.Web/Rendering` — временный HTML-рендеринг;
- `tests/Pismolet.Web.Tests` — unit/integration-тесты текущего среза.

## Команды разработки

```bash
dotnet restore Pismolet.sln
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
```

После запуска:

- главная страница: `/`;
- регистрация: `/account/register`;
- вход: `/account/login`;
- личный кабинет: `/dashboard`;
- создание рассылки: `/mailings/new`;
- карточка рассылки: `/mailings/{id}`;
- загрузка адресов: `/mailings/{id}/recipients`;
- подтверждение базы: `/mailings/{id}/declaration`;
- редактор письма: `/mailings/{id}/message`;
- проверка и fake-оплата: `/mailings/{id}/payment`;
- проверка перед отправкой: `/mailings/{id}/checks`;
- запуск и статус fake-отправки: `/mailings/{id}/send`;
- публичная отписка: `/unsubscribe/{token}` или `/u/{token}`;
- fake provider webhook: `POST /webhooks/email/fake`;
- dev fake webhook sender: `/dev/webhooks/fake` только в Development-окружении;
- админ-зона: `/admin`;
- очередь модерации: `/admin/moderation`;
- изменение дневного лимита клиента: `/admin/limits`;
- fake mailer: `/dev/fake-mailer` только в Development-окружении;
- health-check: `/health`.

## Импорт адресов в Sprint 2

Текущий dev-срез принимает CSV-файл с обязательной колонкой `email`. XLSX поддерживается через ClosedXML.

## Sprint 3: декларация базы и редактор письма

После импорта CSV пользователь проходит шаг подтверждения базы, выбирает источник адресов и тип письма. Затем пользователь заполняет имя отправителя, тему и plain text текст письма.

В preview письма показываются пользовательский текст и автоматически добавленные служебные блоки. Preview не создаёт рабочий unsubscribe token: реальные token создаются только при фактической отправке конкретному адресату.

## Sprint 6: очередь отправки и fake email provider

Рабочий dev-flow:

1. Создать рассылку.
2. Загрузить CSV с колонкой `email`.
3. Подтвердить базу.
4. Сохранить plain text письмо.
5. Пройти fake-оплату.
6. Запустить проверку перед отправкой.
7. Получить статус «Одобрено» автоматически или через `/admin/moderation`.
8. Открыть `/mailings/{id}/send` и нажать «Запустить отправку».
9. Обновить страницу `/send`, чтобы увидеть прогресс и итоговую статистику.

Fake provider не отправляет реальные письма. Тестовые адреса:

- `ok@example.test` — успешная fake-отправка;
- `please-fail@example.test` — provider error;
- `temp@example.test` — временная fake-ошибка.

Дневной лимит берётся из `ClientProfile.DailySendLimit`. Изменить его можно через `/admin/limits`. Для MVP-дня используется UTC-день.

### Storage и очередь отправки

При `Persistence:Provider=InMemory` используется dev fallback:

- `InMemorySendEventRepository`;
- `InMemoryProviderWebhookEventRepository`;
- `InMemoryClientSuppressionRepository`;
- `InlineMailingSendQueue`.

При Postgres provider используется production-срез:

- `EfSendEventRepository`;
- таблица `send_events` через EF migration `20260618000000_AddSendEvents`;
- таблицы `provider_webhook_events` и `client_suppressions` через migration `20260621000000_AddProviderWebhooksAndClientSuppressions`;
- Hangfire server;
- Hangfire PostgreSQL storage со схемой `hangfire` по умолчанию.

Настройки:

```json
{
  "Persistence": {
    "Provider": "Postgres"
  },
  "ConnectionStrings": {
    "PismoletDb": "Host=localhost;Port=5432;Database=pismolet;Username=pismolet;Password=pismolet"
  },
  "Sending": {
    "BatchSize": "100",
    "Queue": "Hangfire"
  },
  "Hangfire": {
    "SchemaName": "hangfire",
    "WorkerCount": "1"
  },
  "Unsubscribe": {
    "Secret": "change-me-in-production",
    "TokenLifetimeDays": "90"
  },
  "Webhooks": {
    "FakeProviderSecret": "change-me-for-non-dev",
    "FakeSenderEnabled": "false"
  }
}
```

Для временного smoke test без Hangfire можно оставить Postgres persistence, но включить inline queue:

```json
{
  "Sending": {
    "Queue": "Inline"
  }
}
```

Smoke test для Postgres + Hangfire:

1. Поднять PostgreSQL и задать `ConnectionStrings:PismoletDb`.
2. Запустить приложение в `Development`, чтобы `MigratePismoletDatabase()` применил migration.
3. Пройти flow до `/mailings/{id}/send`.
4. Нажать «Запустить отправку».
5. Проверить, что в БД появились строки в `send_events`.
6. Перезапустить приложение и открыть `/mailings/{id}/send`.
7. Убедиться, что статистика отправки сохранилась после рестарта.
8. Проверить, что Hangfire создал служебные таблицы в схеме `hangfire`.

## Sprint 7: глобальная отписка

Рабочий flow:

1. Отправить рассылку через fake provider.
2. Открыть `/dev/fake-mailer` и найти fake-письмо получателю.
3. Открыть unsubscribe link без авторизации.
4. Нажать «Отписаться».
5. Повторить POST/открытие ссылки и убедиться, что операция идемпотентна.
6. Загрузить новый CSV с этим email и проверить агрегированный счётчик «Исключены по глобальной отписке».
7. Импортировать email до отписки, затем отписать его перед отправкой и проверить, что отправка создаёт skipped event без provider message id.
8. Перезапустить приложение с Postgres provider и убедиться, что запись в `global_suppressions` сохранилась.

Таблица `global_suppressions` расширяется миграцией `20260620000000_UpgradeGlobalSuppressions`. Клиентский UI не показывает список конкретных отписавшихся адресов, только агрегированные счётчики.

## Sprint 8: webhooks delivery/bounce/complaint

Fake webhook endpoint:

```bash
curl -X POST http://localhost:5000/webhooks/email/fake \
  -H 'Content-Type: application/json' \
  -H 'X-Pismolet-Webhook-Secret: dev-fake-webhook-secret' \
  -d '{
    "providerEventId":"evt-delivered-1",
    "providerMessageId":"fake-message-id-from-send-event",
    "eventType":"delivered",
    "occurredAt":"2026-06-20T12:00:00Z"
  }'
```

Поддерживаются `eventType`: `accepted`, `delivered`, `soft_bounce`, `hard_bounce`, `complaint`, `rejected`, `unknown`.

Dev-проверка:

1. Отправить рассылку через fake provider.
2. Открыть `/dev/webhooks/fake`.
3. Вставить `ProviderMessageId` из dev-сводки/БД.
4. Отправить `delivered` и проверить блок «Доставка» на `/mailings/{id}/send`.
5. Повторить тот же event id и убедиться, что счётчики не удвоились.
6. Отправить `hard_bounce` и убедиться, что этот email попал в `client_suppressions` и у этого клиента исключается при следующем импорте/отправке.
7. Отправить `complaint` и убедиться, что email попал в `global_suppressions` и исключается для всех клиентов.

Оставшиеся ограничения: payment/risk/moderation storage из Sprint 4–5 всё ещё частично in-memory; для полного production persistence их нужно перенести в EF/PostgreSQL отдельным срезом.
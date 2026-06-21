# Локальный запуск Письмолёта

Документ описывает два поддерживаемых режима Sprint 11: быстрый dev/in-memory для полного MVP e2e и Postgres+Hangfire smoke для устойчивых storage/queue частей.

## Требования

- .NET SDK 9.
- Для Postgres smoke: PostgreSQL 15+ или совместимый контейнер.
- Реальные email/payment провайдеры для MVP smoke не нужны и должны быть выключены.

## Команды разработки

```bash
dotnet restore Pismolet.sln
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
```

## Режим A: dev / in-memory full e2e

Минимальные настройки:

```json
{
  "Persistence": {
    "Provider": "InMemory"
  },
  "MailProvider": "FakeMailer",
  "Admin": {
    "AllowedEmails": "demo@pismolet.local;admin@example.test"
  },
  "Webhooks": {
    "FakeProviderSecret": "dev-fake-webhook-secret",
    "FakeSenderEnabled": "true"
  },
  "Unsubscribe": {
    "Secret": "dev-unsubscribe-secret"
  },
  "InboundReplies": {
    "Secret": "dev-inbound-reply-secret",
    "Domain": "reply.pismolet.local"
  }
}
```

После запуска:

- `/` — главная;
- `/account/register` — регистрация;
- `/account/login` — вход;
- `/dashboard` — личный кабинет;
- `/dev/fake-mailer` — fake письма;
- `/dev/webhooks/fake` — генератор fake delivery/bounce/complaint;
- `/dev/replies/fake` — генератор fake inbound reply;
- `/admin` — админка при попадании email в allowlist;
- `/health` — health check.

Dev seed запускается только в `Development` и не запускается под test host. Demo-пользователь: `demo@pismolet.local`, пароль `password123`. Demo email-адреса используют `.test` или `.local`, реальные получатели не нужны.

## Режим B: Postgres + Hangfire smoke

Пример настроек:

```json
{
  "Persistence": {
    "Provider": "Postgres"
  },
  "ConnectionStrings": {
    "PismoletDb": "Host=localhost;Port=5432;Database=pismolet;Username=pismolet;Password=pismolet"
  },
  "Sending": {
    "Queue": "Hangfire",
    "BatchSize": "100"
  },
  "Hangfire": {
    "SchemaName": "hangfire",
    "WorkerCount": "1"
  },
  "MailProvider": "FakeMailer",
  "Webhooks": {
    "FakeProviderSecret": "dev-fake-webhook-secret",
    "FakeSenderEnabled": "true"
  },
  "Admin": {
    "AllowedEmails": "admin@example.test"
  }
}
```

Проверяемые устойчивые части:

- `global_suppressions`;
- `send_events`;
- `provider_webhook_events`;
- `client_suppressions`;
- `reply_events`;
- Hangfire storage и очереди `mailing`, `reply`, `cleanup`.

Важно: production persistence для payment/risk/moderation/settings не является P0 Sprint 11, если она не была реализована ранее. Smoke проверяет согласованность уже устойчивых storage/queue частей.

## Миграции

В `Development` приложение вызывает `MigratePismoletDatabase()` на startup. Для ручной проверки ожидайте таблицы:

- users/account tables текущего среза;
- `global_suppressions`;
- `send_events`;
- `provider_webhook_events`;
- `client_suppressions`;
- `reply_events`;
- служебные таблицы Hangfire в схеме `hangfire`.

## Обязательные настройки

- `Persistence:Provider` — `InMemory` или `Postgres`.
- `ConnectionStrings:PismoletDb` — обязательно для Postgres.
- `Sending:Queue` — `Inline` или `Hangfire`.
- `Hangfire:SchemaName`, `Hangfire:WorkerCount` — настройки Hangfire.
- `Unsubscribe:Secret` или `PISMOLET_UNSUBSCRIBE_SECRET` — подпись unsubscribe tokens.
- `InboundReplies:Secret` или `PISMOLET_INBOUND_REPLY_SECRET` — подпись reply tokens.
- `Webhooks:FakeProviderSecret` — secret для fake webhook.
- `Admin:AllowedEmails` или `PISMOLET_ADMIN_EMAILS` — allowlist админов.

## Troubleshooting

### Не применились миграции

Проверьте строку подключения и что `Persistence:Provider=Postgres`. В Development миграции запускаются автоматически. Если migrations отсутствуют или конфликтуют с уже созданной схемой, используйте чистую demo-БД.

### Не работает Hangfire

Проверьте `Sending:Queue=Hangfire`, строку подключения и наличие схемы `hangfire`. Для временной диагностики можно поставить `Sending:Queue=Inline`, но это уже не Hangfire smoke.

### Нет доступа в admin

Проверьте, что текущий email пользователя есть в `Admin:AllowedEmails` или `PISMOLET_ADMIN_EMAILS`. Разделитель: запятая, точка с запятой, пробел или новая строка.

### Webhook отклоняется

Проверьте header `X-Pismolet-Webhook-Secret` и значение `Webhooks:FakeProviderSecret`.

### Unsubscribe token невалиден

Preview всегда показывает `/unsubscribe/example-token`; рабочий token берите из реально отправленного fake письма в `/dev/fake-mailer`.

### Fake provider/payment случайно заменены реальными

Для Sprint 11 smoke используйте `MailProvider=FakeMailer`. Реальные payment provider settings в MVP smoke не используются.

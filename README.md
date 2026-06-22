# Письмолёт

MVP web-сервиса простых email-рассылок без BCC, таблиц и риска заблокировать рабочую почту.

## Development / текущий срез

Ветка `Development` содержит .NET / ASP.NET Core vertical slice приложения.

Реализованный MVP-flow:

```text
регистрация → подтверждение email → профиль клиента → создание рассылки → импорт CSV/XLSX → декларация базы → редактор письма → preview → расчёт стоимости → fake-оплата → risk-проверка → автоодобрение или ручная модерация → запуск fake-отправки → fake delivery/bounce/complaint → публичная отписка → inbound reply → статистика → admin audit
```

Smoke/demo flow использует только fake payment и fake email provider. Реальные платежи и реальные email-отправки в Sprint 11 не подключаются.

## Обязательные документы Sprint 11

- [`docs/local_run.md`](docs/local_run.md) — локальный запуск, dev/in-memory и Postgres+Hangfire smoke.
- [`docs/demo_checklist.md`](docs/demo_checklist.md) — полный ручной MVP e2e checklist.
- [`docs/llm_agent_guide.md`](docs/llm_agent_guide.md) — правила для LLM-агентов и опасные зоны.
- [`docs/mvp_acceptance.md`](docs/mvp_acceptance.md) — критерии приёмки MVP и известные ограничения.

Post-MVP / production планы:

- [`docs/open_tracking_plan.md`](docs/open_tracking_plan.md) — план внедрения аналитики открытий писем.

Demo-файлы:

- [`docs/examples/demo_recipients.csv`](docs/examples/demo_recipients.csv) — основной CSV для happy path.
- [`docs/examples/import_errors.csv`](docs/examples/import_errors.csv) — CSV для проверки ошибок импорта.

## Структура кода

- `src/Pismolet.Domain` — доменные модели и state machines.
- `src/Pismolet.Application` — сценарии, интерфейсы persistence/provider и бизнес-проверки.
- `src/Pismolet.Infrastructure` — EF/PostgreSQL, in-memory fallback, Hangfire, fake provider, SMTP adapter, seed.
- `src/Pismolet.Web` — HTTP endpoints, auth policies, HTML rendering.
- `tests/Pismolet.Web.Tests` — unit/integration tests.
- `source/tasks` — sprint backlog и ручные test plans.

## Быстрые команды

```bash
dotnet restore Pismolet.sln
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
```

Подробности настроек — в [`docs/local_run.md`](docs/local_run.md).

## Основные маршруты

- `/` — главная;
- `/account/register` — регистрация;
- `/account/login` — вход;
- `/dashboard` — личный кабинет;
- `/profile` — профиль клиента;
- `/mailings/new` — создание рассылки;
- `/mailings/{id}` — карточка рассылки;
- `/mailings/{id}/recipients` — импорт адресов;
- `/mailings/{id}/declaration` — подтверждение базы;
- `/mailings/{id}/message` — редактор письма и preview;
- `/mailings/{id}/payment` — расчёт стоимости и fake-оплата;
- `/mailings/{id}/checks` — risk-проверка;
- `/mailings/{id}/send` — отправка и статистика;
- `/unsubscribe/{token}` или `/u/{token}` — публичная отписка;
- `/webhooks/email/fake` — fake delivery/bounce/complaint webhook;
- `/webhooks/email/fake/inbound` — fake inbound reply webhook;
- `/admin` — админка;
- `/health` — health check.

Dev-only маршруты доступны только в `Development`:

- `/dev/fake-mailer`;
- `/dev/webhooks/fake`;
- `/dev/replies/fake`.

## Demo seed

В `Development` автоматически запускается единый seed hook `SeedPismoletDevData()`.

Demo-пользователь:

- email: `demo@pismolet.local`;
- пароль: `password123`.

Seed не запускается в test host и не должен включаться в production-like окружении.

## Production hardening / post-MVP

Не входит в Sprint 11 MVP:

- реальный email provider;
- реальный платёжный агент;
- полная production deliverability-инфраструктура;
- production persistence для всех оставшихся in-memory/dev-срезов;
- полноценная аналитика;
- полный redesign UI;
- мультидоменная отправка от клиентов.

Подробный статус критериев — в [`docs/mvp_acceptance.md`](docs/mvp_acceptance.md).

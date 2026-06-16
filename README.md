# pismolet

MVP web-сервиса простых email-рассылок «Письмолёт».

## Development / Sprint 0

Ветка `Development` содержит стабилизированный .NET / ASP.NET Core срез приложения.

Sprint 0 фиксирует:

- solution `Pismolet.sln`;
- web-проект `src/Pismolet.Web`;
- тестовый проект `tests/Pismolet.Web.Tests`;
- базовый CI для restore, format, build и test;
- dev-only in-memory реализации;
- ADR по статусу .NET-среза;
- инструкцию для LLM-агентов `AGENTS.md`.

## Уже реализованный вертикальный срез

- регистрация пользователя;
- вход и выход;
- подтверждение email через dev/fake mailer;
- создание профиля клиента с лимитами;
- защищённый личный кабинет со списком рассылок;
- audit log ключевых действий.

## Структура кода

- `src/Pismolet.Web/Domain` — доменные модели;
- `src/Pismolet.Web/Application` — сценарии и интерфейсы;
- `src/Pismolet.Web/Infrastructure` — временные in-memory реализации;
- `src/Pismolet.Web/Endpoints` — HTTP endpoints;
- `src/Pismolet.Web/Rendering` — временный HTML-рендеринг;
- `tests/Pismolet.Web.Tests` — unit-тесты текущего среза.

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
- fake mailer: `/dev/fake-mailer` только в Development-окружении;
- health-check: `/health`.

## Архитектурный статус

Основной код ветки `Development` развивается как .NET / ASP.NET Core. Решение зафиксировано в `docs/adr/0001-dotnet-slice-status.md`.

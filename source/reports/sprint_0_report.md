# Отчёт Sprint 0

Дата: 2026-06-16  
Ветка: `Development`  
Статус: выполнен с техническими ограничениями окружения

## Что сделано

- Добавлен `Pismolet.sln`.
- В solution подключены:
  - `src/Pismolet.Web/Pismolet.Web.csproj`;
  - `tests/Pismolet.Web.Tests/Pismolet.Web.Tests.csproj`.
- Добавлен тестовый проект на xUnit.
- Добавлены unit-тесты для:
  - `ClientProfile.NewClientDefault()`;
  - `InMemoryUserRepository`;
  - `InMemoryFakeMailer`;
  - `InMemoryAuditLogger`;
  - `UserAccountService.Register`;
  - `UserAccountService.Authenticate`;
  - `UserAccountService.ConfirmEmail`.
- Добавлен `Directory.Packages.props` для central package management.
- Добавлен `.editorconfig`.
- Добавлен GitHub Actions workflow `.github/workflows/dotnet.yml`.
- Добавлен `AGENTS.md` с правилами для LLM-агентов.
- Добавлен ADR `docs/adr/0001-dotnet-slice-status.md`.
- Добавлен `appsettings.Development.json` с явными dev-настройками.
- Обновлён `README.md`.
- `Program.cs` оставлен composition root и подготовлен для тестового доступа через `public partial class Program`.
- `/dev/fake-mailer` ограничен Development-окружением.
- Cookie settings явно заданы для dev-среза.
- Убрана скрытая зависимость от отсутствующего metadata extension в `AccountEndpoints`.

## Архитектурные решения

Текущий .NET / ASP.NET Core срез считается рабочей основой ветки `Development`. Старое техническое ТЗ требует отдельной синхронизации. Решение зафиксировано в ADR.

Sprint 0 не подключает PostgreSQL, EF Core, Hangfire, Docker Compose и реальные внешние провайдеры, потому что текущий спринт стабилизирует уже созданный in-memory срез и не должен развивать два стека одновременно.

## Dev-only ограничения

- `InMemoryUserRepository` — временное хранилище.
- `InMemoryFakeMailer` — временный dev mailer.
- `InMemoryAuditLogger` — временный audit storage.
- `dev:` password hash остаётся известным риском и должен быть заменён до MVP/production.

## Команды проверки

```bash
dotnet restore Pismolet.sln
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
```

## Ручной smoke-flow

1. Открыть `/health`.
2. Открыть `/`.
3. Зарегистрировать пользователя через `/account/register`.
4. Открыть `/dev/fake-mailer` в Development-окружении.
5. Перейти по ссылке подтверждения.
6. Войти через `/account/login`.
7. Открыть `/dashboard`.
8. Выйти через logout.
9. Проверить, что `/dashboard` без авторизации недоступен.

## Что не удалось проверить в среде чата

Локальный `dotnet restore/build/test/run` не запускался в среде чата, потому что нет полноценного локального clone/runtime-доступа к репозиторию. Проверка должна пройти в GitHub Actions или локально у разработчика.

## Риски для следующего спринта

- Проверить CI после первого запуска workflow.
- Заменить dev password hash на безопасный механизм.
- Решить, подключается ли ASP.NET Core Identity уже в Sprint 1/2.
- Принять решение по синхронизации `docs/platform_tz.md`.
- Добавить web smoke/integration tests после подтверждения допустимого тестового пакета.

# Удалить потом вручную

Файл фиксирует остатки transitional fallback-файлов после переноса слоёв из `Pismolet.Web` в отдельные проекты `Pismolet.Domain`, `Pismolet.Application` и `Pismolet.Infrastructure`.

Эти файлы не должны участвовать в сборке Web-проекта. Сейчас они закрыты правилом `Compile Remove` в `src/Pismolet.Web/Pismolet.Web.csproj`. GitHub connector не дал удалить их автоматически, поэтому после локальной проверки `dotnet build` / `dotnet test` их нужно удалить вручную.

## Файлы для ручного удаления

- `src/Pismolet.Web/Application/Imports/RecipientImportService.cs`
- `src/Pismolet.Web/Application/Mail/IFakeMailer.cs`
- `src/Pismolet.Web/Application/Persistence/IGlobalSuppressionRepository.cs`
- `src/Pismolet.Web/Infrastructure/Mail/InMemoryFakeMailer.cs`
- `src/Pismolet.Web/Infrastructure/Persistence/InMemoryGlobalSuppressionRepository.cs`

## После удаления

1. Проверить, что в новых проектах есть актуальные замены:
   - `src/Pismolet.Application/Imports/ImportServices.cs`
   - `src/Pismolet.Application/Mail/IFakeMailer.cs` или соответствующий контракт в application/domain слое
   - `src/Pismolet.Application/Persistence/RepositoryContracts.cs`
   - `src/Pismolet.Infrastructure/Mail/InMemoryFakeMailer.cs`
   - `src/Pismolet.Infrastructure/Persistence/InMemoryRepositories.cs`
2. Удалить лишние `Compile Remove` из `src/Pismolet.Web/Pismolet.Web.csproj`, если старых папок больше нет.
3. Запустить:

```bash
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
```

## Контекст

Актуальное состояние миграции слоёв описано в:

- `docs/refactor-and-persistence-migration-report.md`
- `docs/sync-sprint-report.md`

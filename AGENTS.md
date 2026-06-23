# Инструкция для LLM-агентов

## Контекст репозитория

Фактический код ветки `Development` сейчас находится в `src/Pismolet.Web` и реализован как .NET / ASP.NET Core web-приложение.

## Правила изменения кода

1. Перед изменениями читать существующий код.
2. Не переписывать стек без отдельного ADR и явного решения владельца проекта.
3. Не добавлять реальные внешние интеграции без отдельной задачи.
4. Пользовательский UI должен быть на русском языке.
5. Для изменений в auth, оплате, отписке, отправке, модерации и лимитах нужны тесты и human review.

## Команды проверки

```bash
dotnet restore Pismolet.sln
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
```

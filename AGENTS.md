# Инструкция для LLM-агентов

## Контекст репозитория

Фактический код ветки `Development` сейчас находится в `src/Pismolet.Web` и реализован как .NET / ASP.NET Core web-приложение.

Задачи спринтов находятся в `source/tasks`. Общий план спринтов находится в `docs/sprints.md`. Целевая .NET-архитектура описана в `docs/architecture_dotnet.md`.

`docs/platform_tz.md` пока содержит более раннее техническое ТЗ под Python/Django. Это архитектурное расхождение нельзя решать молча в коде: требуется ADR и синхронизация документации.

## Правила изменения кода

1. Перед изменениями читать существующий код, а не только ТЗ.
2. Не переписывать стек без отдельного ADR и явного решения владельца проекта.
3. Не добавлять реальные внешние интеграции без отдельной задачи.
4. Dev/fake mailer нельзя заменять реальным email-провайдером в рамках обычного рефакторинга.
5. In-memory repository, fake mailer и audit logger являются временными dev-only реализациями.
6. LLM-модерация не входит в MVP.
7. Пользовательский UI должен быть на русском языке.
8. Пользовательский UI не должен раскрывать внутренние технические сущности вроде `Campaign`, `ImportBatch`, `SendEvent`, `PaymentAttempt`.
9. Для изменений в auth, оплате, отписке, отправке, модерации и лимитах нужны тесты и human review.
10. Импорт адресов в текущем срезе принимает CSV с колонкой `email`; XLSX подключается только отдельным решением по библиотеке и лицензии.

## Команды проверки

```bash
dotnet restore Pismolet.sln
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
```

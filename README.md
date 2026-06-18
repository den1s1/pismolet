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
- проверка адресов: валидные, дубли, невалидные, исключённые по глобальной отписке;
- подтверждение базы адресов после импорта;
- редактор простого plain text письма;
- preview служебных блоков письма;
- fake-оплата MVP;
- формальная проверка перед отправкой и ручная dev/admin модерация;
- fake-отправка Sprint 6 через application queue fallback и `FakeEmailProviderAdapter`;
- dev-only in-memory audit log.

## Структура кода

- `src/Pismolet.Domain` — доменные модели;
- `src/Pismolet.Application` — сценарии и интерфейсы;
- `src/Pismolet.Infrastructure` — persistence, dev fallback и fake-инфраструктура;
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
- создание рассылки: `/mailings/new`;
- карточка рассылки: `/mailings/{id}`;
- загрузка адресов: `/mailings/{id}/recipients`;
- подтверждение базы: `/mailings/{id}/declaration`;
- редактор письма: `/mailings/{id}/message`;
- проверка и fake-оплата: `/mailings/{id}/payment`;
- проверка перед отправкой: `/mailings/{id}/checks`;
- запуск и статус fake-отправки: `/mailings/{id}/send`;
- админ-зона: `/admin`;
- очередь модерации: `/admin/moderation`;
- изменение дневного лимита клиента: `/admin/limits`;
- fake mailer: `/dev/fake-mailer` только в Development-окружении;
- health-check: `/health`.

## Импорт адресов в Sprint 2

Текущий dev-срез принимает CSV-файл с обязательной колонкой `email`. XLSX не подключён: выбор библиотеки и проверка лицензии перенесены в отдельную задачу.

## Sprint 3: декларация базы и редактор письма

После импорта CSV пользователь проходит шаг подтверждения базы, выбирает источник адресов и тип письма. Затем пользователь заполняет имя отправителя, тему и plain text текст письма.

В preview письма показываются пользовательский текст и автоматически добавленные служебные блоки. Публичная обработка страницы отписки будет реализована отдельно.

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

Текущий storage очереди — dev fallback `InlineMailingSendQueue`: он ставит batch-обработку в background task процесса приложения. Это не production-Hangfire. `SendEvent` в этом срезе хранится через in-memory репозиторий; EF/PostgreSQL migration для `send_events` остаётся обязательным техническим долгом перед production/dev-restart проверкой.

Временные in-memory реализации не являются production-хранилищем. Целевая замена — EF Core / PostgreSQL и Hangfire/PostgreSQL storage по архитектурному документу.

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
- dev-only in-memory audit log.

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
- создание рассылки: `/mailings/new`;
- карточка рассылки: `/mailings/{id}`;
- загрузка адресов: `/mailings/{id}/recipients`;
- подтверждение базы: `/mailings/{id}/declaration`;
- редактор письма: `/mailings/{id}/message`;
- fake mailer: `/dev/fake-mailer` только в Development-окружении;
- health-check: `/health`.

## Импорт адресов в Sprint 2

Текущий dev-срез принимает CSV-файл с обязательной колонкой `email`. XLSX не подключён: выбор библиотеки и проверка лицензии перенесены в отдельную задачу.

## Sprint 3: декларация базы и редактор письма

После импорта CSV пользователь проходит шаг подтверждения базы, выбирает источник адресов и тип письма. Затем пользователь заполняет имя отправителя, тему и plain text текст письма. HTML-редактор, реальная отправка, расчёт стоимости и оплата остаются за пределами Sprint 3.

В preview письма показываются пользовательский текст и автоматически добавленные служебные блоки. Публичная обработка страницы отписки будет реализована отдельно.

Временные in-memory реализации не являются production-хранилищем. Целевая замена — EF Core / PostgreSQL и фоновые задачи по архитектурному документу.

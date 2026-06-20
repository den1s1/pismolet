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
- fake-отправка Sprint 6 через `FakeEmailProviderAdapter`;
- публичная unsubscribe-страница Sprint 7;
- устойчивый EF/PostgreSQL `global_suppressions` для глобальной отписки;
- production-срез очереди: Hangfire + PostgreSQL storage для Postgres provider;
- production-срез persistence: EF/PostgreSQL `send_events`;
- dev-only in-memory audit log.

## Структура кода

- `src/Pismolet.Domain` — доменные модели;
- `src/Pismolet.Application` — сценарии и интерфейсы;
- `src/Pismolet.Infrastructure` — persistence, dev fallback, Hangfire и fake-инфраструктура;
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
- публичная отписка: `/unsubscribe/{token}` или `/u/{token}`;
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

### Реальная SMTP-отправка для личного теста

По умолчанию рассылки не уходят наружу. Для проверки на своих ящиках можно явно включить SMTP provider:

```bash
export PISMOLET_SENDING_PROVIDER=Smtp
export PISMOLET_PUBLIC_BASE_URL='https://pismolet.ru'
export PISMOLET_SMTP_HOST=smtp.example.com
export PISMOLET_SMTP_PORT=587
export PISMOLET_SMTP_ENABLE_SSL=true
export PISMOLET_SMTP_USERNAME='smtp-user@example.com'
export PISMOLET_SMTP_PASSWORD='smtp-password-or-app-password'
export PISMOLET_SMTP_FROM_EMAIL='smtp-user@example.com'
export PISMOLET_SMTP_FROM_NAME='Письмолет'

ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj --no-launch-profile
```

Используйте отдельный тестовый SMTP-ящик и CSV только с личными адресами. Для Gmail/Yandex/Mail.ru обычно нужен пароль приложения, а не основной пароль от аккаунта.

Для доменной почты Timeweb проверенный вариант:

```bash
export PISMOLET_SENDING_PROVIDER=Smtp
export PISMOLET_PUBLIC_BASE_URL='https://pismolet.ru'
export PISMOLET_SMTP_HOST=smtp.timeweb.ru
export PISMOLET_SMTP_PORT=2525
export PISMOLET_SMTP_ENABLE_SSL=true
export PISMOLET_SMTP_USERNAME='info@pismolet.ru'
export PISMOLET_SMTP_PASSWORD='smtp-password'
export PISMOLET_SMTP_FROM_EMAIL='info@pismolet.ru'
export PISMOLET_SMTP_FROM_NAME='Письмолёт'
```

`PISMOLET_PUBLIC_BASE_URL` нужен для абсолютных ссылок отписки и заголовков `List-Unsubscribe`.

Дневной лимит берётся из `ClientProfile.DailySendLimit`. Изменить его можно через `/admin/limits`. Для MVP-дня используется UTC-день.

### Минимальный запуск на сервере

Серверу нужны .NET 9 SDK/runtime, PostgreSQL и внешний HTTPS-домен, который проксирует приложение.

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5080
export PISMOLET_CONNECTION_STRING='Host=127.0.0.1;Port=5432;Database=pismolet;Username=pismolet;Password=postgres-password'
export PISMOLET_UNSUBSCRIBE_SECRET='long-random-secret'
export PISMOLET_PUBLIC_BASE_URL='https://pismolet.ru'

export PISMOLET_SENDING_PROVIDER=Smtp
export PISMOLET_SMTP_HOST=smtp.timeweb.ru
export PISMOLET_SMTP_PORT=2525
export PISMOLET_SMTP_ENABLE_SSL=true
export PISMOLET_SMTP_USERNAME='info@pismolet.ru'
export PISMOLET_SMTP_PASSWORD='smtp-password'
export PISMOLET_SMTP_FROM_EMAIL='info@pismolet.ru'
export PISMOLET_SMTP_FROM_NAME='Письмолёт'
```

Перед первым запуском применить миграции:

```bash
dotnet tool install --global dotnet-ef --version 9.* || dotnet tool update --global dotnet-ef --version 9.*
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef database update \
  --project src/Pismolet.Infrastructure/Pismolet.Infrastructure.csproj \
  --startup-project src/Pismolet.Web/Pismolet.Web.csproj
```

Запуск приложения:

```bash
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj --no-launch-profile
```

### Storage и очередь отправки

При `Persistence:Provider=InMemory` используется dev fallback:

- `InMemorySendEventRepository`;
- `InlineMailingSendQueue`.

При Postgres provider используется production-срез:

- `EfSendEventRepository`;
- таблица `send_events` через EF migration `20260618000000_AddSendEvents`;
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
  "Public": {
    "BaseUrl": "https://pismolet.ru"
  },
  "Hangfire": {
    "SchemaName": "hangfire",
    "WorkerCount": "1"
  },
  "Unsubscribe": {
    "Secret": "change-me-in-production",
    "TokenLifetimeDays": "90"
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

Оставшиеся ограничения: payment/risk/moderation storage из Sprint 4–5 всё ещё частично in-memory; для полного production persistence их нужно перенести в EF/PostgreSQL отдельным срезом.

# Спринт синхронизации — закрытие долгов Sprint 0–3

Ветка: `Development`  
Основание: `docs/sprints.md`, разделы Sprint 0–3  
Дата составления: 2026-06-16  
Статус: backlog для выравнивания кода с уже принятым планом спринтов

## 1. Назначение спринта

Цель спринта синхронизации — довести текущий код до уровня, который уже должен быть обеспечен по `docs/sprints.md` для Sprint 0–3, не начиная полноценную реализацию Sprint 4.

Спринт не должен добавлять расчёт стоимости, fake-оплату, risk-check, модерацию или отправку писем. Эти темы остаются в Sprint 4+.

## 2. Текущий срез кода

По текущему состоянию ветки `Development` реализован пользовательский vertical slice:

```text
регистрация → подтверждение email → вход → ЛК → создание рассылки → CSV-импорт → декларация базы → редактор plain text письма → preview служебных блоков
```

Уже есть:

- регистрация, вход, выход;
- dev/fake подтверждение email;
- `ClientProfile` с базовыми лимитами;
- защищённый `/dashboard`;
- создание черновика рассылки;
- CSV-импорт с колонкой `email`;
- нормализация email;
- проверка дублей;
- синтаксическая проверка email;
- проверка глобальной отписки;
- результат импорта в UI;
- декларация базы;
- редактор plain text письма;
- preview служебных блоков письма;
- dev-only in-memory audit log;
- unit-тесты для части auth/import/mailing-flow.

Ключевые расхождения с `docs/sprints.md`:

- целевая структура `src/Pismolet.Web`, `src/Pismolet.Application`, `src/Pismolet.Domain`, `src/Pismolet.Infrastructure` пока не выделена;
- persistence остаётся in-memory, нет PostgreSQL, EF Core и migrations;
- нет Docker Compose;
- нет CI workflow;
- нет XLSX-импорта;
- нет Testcontainers и отдельного проекта интеграционных тестов;
- UI построен через minimal endpoints + `HtmlRenderer`, а план требует ASP.NET Core Razor Pages / Razor layout / partial views;
- audit log не сохраняется в БД;
- нет seed/demo-данных;
- нет пустой защищённой admin-зоны из Sprint 0;
- модель импорта не выделяет полноценный `ImportBatch` как сущность;
- часть тестов из `docs/sprints.md` отсутствует или покрыта только частично.

## 3. Рамки спринта

Входит:

- закрытие долгов Sprint 0–3;
- инфраструктура, persistence, тесты и импорт XLSX;
- минимальная admin-заглушка;
- приведение документации текущего среза к фактическому состоянию.

Не входит:

- Sprint 4: расчёт стоимости и fake-оплата;
- Sprint 5: risk-check и модерация;
- Sprint 6: очередь отправки и fake email provider для массовой отправки;
- Sprint 7: публичная unsubscribe-страница;
- Sprint 8+: webhooks и production email provider.

## 4. Задачи реализации

### SYNC-01. Разделить solution на целевые проекты

**Цель:** привести структуру к `docs/sprints.md` без изменения пользовательского flow.

Задачи:

- Создать проекты:
  - `src/Pismolet.Domain`;
  - `src/Pismolet.Application`;
  - `src/Pismolet.Infrastructure`;
  - оставить `src/Pismolet.Web` только для UI/endpoints/composition root.
- Перенести доменные модели из `Pismolet.Web/Domain` в `Pismolet.Domain`.
- Перенести application services и интерфейсы в `Pismolet.Application`.
- Перенести in-memory persistence, fake mailer и audit-инфраструктуру в `Pismolet.Infrastructure`.
- Настроить project references:
  - `Web` → `Application`, `Infrastructure`;
  - `Application` → `Domain`;
  - `Infrastructure` → `Application`, `Domain`;
  - `Domain` ни от кого не зависит.
- Обновить namespaces.
- Обновить `Pismolet.sln`.

Acceptance criteria:

- `dotnet build Pismolet.sln` проходит.
- `dotnet test Pismolet.sln` проходит.
- Пользовательский flow Sprint 1–3 не меняется.
- `Pismolet.Domain` не зависит от ASP.NET Core.

### SYNC-02. Подключить PostgreSQL и EF Core migrations

**Цель:** закрыть долг Sprint 0 по PostgreSQL/EF Core.

Задачи:

- Добавить EF Core packages в инфраструктурный проект.
- Добавить `PismoletDbContext`.
- Описать entity configurations для:
  - `User`;
  - `ClientProfile`;
  - `Mailing`;
  - `Recipient`;
  - `GlobalSuppression`;
  - `AuditRecord`;
  - `MailingDeclaration`;
  - `MailingMessageDraft`.
- Добавить первую migration.
- Настроить connection string через configuration.
- Зарегистрировать EF repositories в DI.
- Оставить in-memory repositories как dev/test alternative, если это упростит тесты.
- Добавить безопасный режим локального запуска без production-секретов.

Acceptance criteria:

- Приложение может стартовать с PostgreSQL.
- Миграции применяются локально.
- Данные пользователей, рассылок, импортов, деклараций и писем переживают restart приложения.
- In-memory режим не используется по умолчанию как основной persistence для `Development`.

### SYNC-03. Добавить Docker Compose для локального запуска

**Цель:** закрыть требование Sprint 0 о Docker Compose.

Задачи:

- Добавить `docker-compose.yml` с PostgreSQL.
- Добавить `.env.example` или комментарии по переменным окружения.
- Настроить volume для PostgreSQL.
- Обновить README с командами запуска.
- Проверить, что приложение подключается к БД из локального окружения.

Acceptance criteria:

- `docker compose up -d` поднимает PostgreSQL.
- После запуска приложения `/health` отвечает `ok`.
- README содержит актуальные команды.

### SYNC-04. Добавить CI workflow

**Цель:** закрыть требование Sprint 0 по CI: build, format/lint, tests.

Задачи:

- Добавить `.github/workflows/ci.yml`.
- Настроить шаги:
  - checkout;
  - setup .NET;
  - `dotnet restore`;
  - `dotnet format --verify-no-changes`;
  - `dotnet build --no-restore`;
  - `dotnet test --no-build`.
- Убедиться, что workflow работает для веток `main` и `Development`.
- При необходимости зафиксировать версию .NET через `global.json`.

Acceptance criteria:

- Pull request или push запускает CI.
- CI падает при форматировании, build error или failing tests.

### SYNC-05. Реализовать XLSX-импорт

**Цель:** закрыть недостающую часть Sprint 2.

Задачи:

- Выбрать библиотеку для XLSX с допустимой лицензией для проекта.
- Зафиксировать выбор в коротком ADR или в `docs/platform_tz.md`.
- Расширить импорт так, чтобы сервис принимал `.csv` и `.xlsx`.
- Для XLSX читать первый лист.
- Требовать колонку `email`.
- Применять те же правила, что для CSV:
  - нормализация;
  - проверка дублей;
  - синтаксическая проверка;
  - глобальная отписка;
  - лимит строк;
  - статистика импорта.
- Обновить UI upload form: `.csv, .xlsx`.
- Обновить ошибки на русском языке.

Acceptance criteria:

- Пользователь может загрузить `.xlsx` с колонкой `email`.
- Результат импорта для XLSX совпадает по правилам с CSV.
- Unit-тесты покрывают XLSX: валидные адреса, дубли, невалидные, отсутствие колонки `email`.

### SYNC-06. Выделить `ImportBatch` как сущность

**Цель:** привести Sprint 2 к модели из `docs/sprints.md`, где импорт является отдельной сущностью.

Задачи:

- Добавить доменную сущность `ImportBatch`.
- Поля:
  - `Id`;
  - `MailingId`;
  - `FileName`;
  - `SourceFormat` (`Csv` / `Xlsx`);
  - `CreatedAt`;
  - `TotalRows`;
  - `Accepted`;
  - `Duplicates`;
  - `Invalid`;
  - `GloballySuppressed`;
  - `Status`.
- Связать `Recipient` с `ImportBatchId`.
- Обновить repository и EF mapping.
- В UI по-прежнему показывать простой результат без технического названия `ImportBatch`.

Acceptance criteria:

- Каждый импорт создаёт отдельный batch.
- Повторная загрузка файла не затирает историю предыдущего импорта.
- Последний результат импорта по-прежнему доступен на карточке рассылки.

### SYNC-07. Перевести audit log из in-memory в persistence

**Цель:** закрыть требования Sprint 0–3 по audit log.

Задачи:

- Добавить таблицу/сущность `AuditRecord` в EF.
- Сохранять события:
  - `registration`;
  - `email_confirmed`;
  - `login`;
  - `mailing_created`;
  - `recipients_import_started`;
  - `recipients_import_completed`;
  - `recipients_import_failed`;
  - `mailing_declaration_confirmed`;
  - `mailing_message_saved`.
- Сохранять:
  - дату;
  - пользователя;
  - IP;
  - user-agent;
  - тип события;
  - контекст/id сущности;
  - версию декларации, где применимо.
- Оставить in-memory audit logger только для unit-тестов.

Acceptance criteria:

- Audit события доступны после restart приложения.
- Интеграционный тест подтверждает запись audit log для регистрации, импорта и декларации.

### SYNC-08. Добавить отдельный проект интеграционных тестов

**Цель:** закрыть обязательные интеграционные тесты Sprint 0–3.

Задачи:

- Создать `tests/Pismolet.IntegrationTests`.
- Подключить:
  - `Microsoft.AspNetCore.Mvc.Testing`;
  - `Testcontainers` для PostgreSQL;
  - xUnit;
  - FluentAssertions.
- Настроить запуск приложения на тестовой БД.
- Покрыть сценарии:
  - `/health`;
  - применение migrations;
  - регистрация пользователя;
  - подтверждение email через fake mailer/dev-механику;
  - вход пользователя;
  - доступ к `/dashboard` только авторизованному пользователю;
  - создание `ClientProfile`;
  - создание рассылки;
  - CSV-импорт;
  - XLSX-импорт;
  - сценарий `импорт → декларация → редактор письма`;
  - сохранение audit log.

Acceptance criteria:

- `dotnet test Pismolet.sln` запускает unit и integration tests.
- Интеграционные тесты используют отдельную временную PostgreSQL БД.

### SYNC-09. Довести unit-тесты до списка из `docs/sprints.md`

**Цель:** закрыть частично отсутствующее unit-покрытие Sprint 0–3.

Задачи:

- Добавить/проверить тесты Sprint 0:
  - создание `ClientProfile`;
  - базовые value objects: email, money/price placeholder, campaign/mailing status;
  - невозможные переходы статусов, если статусная модель уже есть.
- Добавить/проверить тесты Sprint 1:
  - дефолтные лимиты нового клиента;
  - статус клиента `active / blocked`;
  - audit log регистрации, входа и подтверждения email.
- Добавить/проверить тесты Sprint 2:
  - нормализация email;
  - дубли;
  - валидация;
  - suppression list;
  - подсчёт статистики;
  - CSV и XLSX.
- Добавить/проверить тесты Sprint 3:
  - без декларации нельзя перейти к оплате/следующему шагу;
  - рекламное письмо требует рекламного согласия;
  - версия декларации сохраняется;
  - генерация служебного блока письма;
  - генерация unsubscribe token;
  - сохранение письма;
  - обязательные поля письма.

Acceptance criteria:

- В тестах явно отражены обязательные проверки Sprint 0–3.
- Названия тестов читаемы для LLM-агента и человека.

### SYNC-10. Добавить защищённую admin-заглушку

**Цель:** закрыть видимый результат Sprint 0: пустая защищённая admin-зона.

Задачи:

- Добавить route `/admin`.
- Ограничить доступ авторизацией.
- На первом этапе показывать заглушку:
  - «Админ-зона»;
  - текущие системные блоки MVP;
  - пометка, что модерация появится в Sprint 5.
- Не давать обычному неавторизованному пользователю открыть `/admin`.
- Если роли ещё не реализованы, явно зафиксировать временное правило доступа в задаче/комментарии.

Acceptance criteria:

- Неавторизованный пользователь перенаправляется на login.
- Авторизованный пользователь видит admin-заглушку или получает понятный отказ, если введены роли.
- Нет production-функций модерации, только placeholder.

### SYNC-11. Начать переход UI к Razor Pages / layout / partials

**Цель:** закрыть архитектурный долг Sprint 0, не ломая текущий flow.

Задачи:

- Добавить Razor Pages infrastructure.
- Перенести общий HTML layout из `HtmlRenderer.Page` в Razor layout.
- Выделить partials для:
  - header/nav;
  - flash/error block;
  - карточка рассылки;
  - статус/следующий шаг;
  - результат импорта;
  - preview письма.
- На первом проходе перенести не обязательно все страницы, но зафиксировать порядок миграции.
- Не менять внешний русский пользовательский язык.

Acceptance criteria:

- В проекте есть Razor layout.
- Хотя бы dashboard и карточка рассылки используют Razor/partials или есть отдельная задача миграции каждой страницы.
- `HtmlRenderer` либо сокращён, либо помечен как временный debt с планом удаления.

### SYNC-12. Добавить seed/demo-данные

**Цель:** закрыть требование Sprint 0 и упростить ручные проверки.

Задачи:

- Добавить dev seed для:
  - demo-пользователя;
  - подтверждённого email;
  - demo-рассылки;
  - нескольких recipients;
  - suppression email.
- Seed должен работать только в Development/test окружении.
- Данные не должны попадать в production.
- Обновить README с demo-доступами, если они нужны.

Acceptance criteria:

- Локально можно быстро открыть ЛК и проверить flow без ручной регистрации.
- Seed не активируется в production environment.

### SYNC-13. Синхронизировать README и документацию

**Цель:** чтобы документация не утверждала больше/меньше, чем реально реализовано.

Задачи:

- Обновить README после реализации sync sprint.
- Указать:
  - какие хранилища доступны;
  - как запустить PostgreSQL;
  - как применить migrations;
  - как запустить unit/integration tests;
  - как загрузить CSV/XLSX;
  - какие части всё ещё остаются Sprint 4+.
- При выборе XLSX-библиотеки обновить `docs/platform_tz.md` или ADR.
- При переносе по проектам обновить описание структуры кода.

Acceptance criteria:

- Новый разработчик или LLM-агент может запустить проект по README.
- README не называет in-memory основной реализацией, если уже подключён PostgreSQL.

## 5. Рекомендуемый порядок выполнения

1. `SYNC-01` — разделить solution на слои.
2. `SYNC-02` — PostgreSQL / EF Core / migrations.
3. `SYNC-03` — Docker Compose.
4. `SYNC-04` — CI.
5. `SYNC-08` — integration tests foundation.
6. `SYNC-05` — XLSX-импорт.
7. `SYNC-06` — `ImportBatch`.
8. `SYNC-07` — persistent audit log.
9. `SYNC-09` — добить unit-тесты.
10. `SYNC-10` — admin-заглушка.
11. `SYNC-11` — Razor/layout/partials.
12. `SYNC-12` — seed/demo-данные.
13. `SYNC-13` — документация.

## 6. Definition of Done

Спринт синхронизации считается завершённым, когда:

- структура solution соответствует `docs/sprints.md` или отклонение явно зафиксировано;
- приложение запускается с PostgreSQL;
- есть EF migrations;
- есть Docker Compose;
- есть CI;
- CSV и XLSX импорт работают по единым правилам;
- есть отдельный проект интеграционных тестов с Testcontainers;
- audit log сохраняется не только in-memory;
- есть admin-заглушка;
- документация обновлена;
- `dotnet format Pismolet.sln --verify-no-changes` проходит;
- `dotnet build Pismolet.sln` проходит;
- `dotnet test Pismolet.sln` проходит.

## 7. Документы для синхронизации после выполнения

После закрытия задач нужно обновить:

- `README.md` — фактический запуск, структура, команды, ограничения;
- `docs/sprints.md` — отметить закрытые долги Sprint 0–3 или добавить статус выполнения;
- `docs/platform_tz.md` — PostgreSQL/EF, Docker Compose, XLSX-библиотека, Testcontainers;
- ADR по XLSX-библиотеке, если ADR-папка уже заведена или будет заведена в рамках `SYNC-05`.

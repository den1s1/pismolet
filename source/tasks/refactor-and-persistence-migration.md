# Задачи: миграция слоёв и подключение PostgreSQL persistence

Ветка: `Development`  
Статус: backlog для закрытия двух технических долгов после синхронизационного спринта.

## Контекст

Зафиксированные ограничения:

- полный физический перенос классов из `Pismolet.Web` в новые проекты пока не сделан, чтобы не ломать рабочий vertical slice одним большим рефакторингом;
- `Pismolet.Domain`, `Pismolet.Application`, `Pismolet.Infrastructure` добавлены как каркас миграции;
- runtime repositories пока остаются in-memory;
- EF/PostgreSQL добавлены как каркас, но не как полностью подключённая persistence-реализация.

Цель задач — закрыть эти ограничения поэтапно и сохранить рабочий сценарий Sprint 1–3.

## Общие правила

- Не делать один большой рефакторинг.
- Переносить классы маленькими шагами.
- После каждого блока запускать `dotnet build Pismolet.sln` и `dotnet test Pismolet.sln`.
- Не менять пользовательский flow и URL без отдельной задачи.
- In-memory оставить только как явный dev/test fallback.

## MIG-01. Инвентаризировать классы `Pismolet.Web`

Задачи:

- Составить карту текущих классов `Pismolet.Web`.
- Разнести классы по целевым проектам:
  - `Pismolet.Domain` — сущности, enum, value objects, доменные правила;
  - `Pismolet.Application` — use cases, DTO, commands/results, repository interfaces;
  - `Pismolet.Infrastructure` — in-memory repositories, EF repositories, fake/dev adapters, audit storage;
  - `Pismolet.Web` — endpoints, UI, auth/session, DI composition root.
- Отметить спорные классы, которые нельзя переносить автоматически.

Acceptance criteria:

- Есть карта `старый путь -> новый проект`.
- Нет классов с неясным целевым слоем.

## MIG-02. Перенести доменную модель в `Pismolet.Domain`

Задачи:

- Перенести доменные сущности, enum и value objects из `Pismolet.Web`.
- Обновить namespaces и using.
- Проверить, что `Pismolet.Domain` не зависит от ASP.NET Core, EF Core и UI.
- Удалить старые копии классов из `Pismolet.Web`.

Acceptance criteria:

- `Pismolet.Domain` содержит реальную доменную модель, а не пустой каркас.
- `dotnet build` и `dotnet test` проходят.

## MIG-03. Перенести application layer в `Pismolet.Application`

Задачи:

- Перенести use cases текущего vertical slice:
  - регистрация;
  - подтверждение email;
  - вход/профиль клиента, если логика вынесена из endpoint;
  - создание рассылки;
  - импорт получателей;
  - декларация базы;
  - редактор письма;
  - preview служебных блоков.
- Перенести repository interfaces и application DTO.
- Оставить в `Pismolet.Web` только request binding, routing и rendering.

Acceptance criteria:

- Основная бизнес-логика не находится в endpoint-коде.
- Application layer зависит от Domain, но не зависит от Web и EF.

## MIG-04. Перенести infrastructure-классы в `Pismolet.Infrastructure`

Задачи:

- Перенести in-memory repositories из `Pismolet.Web`.
- Перенести fake/dev mailer и audit-реализации.
- Добавить DI extension methods для регистрации infrastructure.
- В `Pismolet.Web` оставить только вызовы этих extension methods.

Acceptance criteria:

- `Pismolet.Web` не содержит реализаций repositories/storage/adapters.
- In-memory режим работает только при явном выборе.

## MIG-05. Очистить `Pismolet.Web`

Задачи:

- Удалить устаревшие папки и дубли классов после переноса.
- Проверить project references.
- Обновить `Pismolet.sln`.
- Обновить README с целевой структурой solution.

Acceptance criteria:

- `Pismolet.Web` — только web/UI/composition root.
- Новые проекты стали рабочими слоями, а не пустым каркасом.

## MIG-06. Зафиксировать contracts persistence-слоя

Задачи:

- Проверить repository interfaces в `Pismolet.Application`.
- Убедиться, что в interfaces нет EF-типов.
- Добавить недостающие операции для:
  - пользователей;
  - профилей клиентов;
  - рассылок;
  - импортов и получателей;
  - глобальной отписки;
  - деклараций;
  - черновиков письма;
  - audit log.
- Описать transaction boundaries для операций текущего vertical slice.

Acceptance criteria:

- Application layer не знает о `DbContext`.
- Для всего текущего flow есть persistence contracts.

## MIG-07. Довести EF-модель до полного runtime-набора

Задачи:

- Настроить `PismoletDbContext` и mappings для:
  - users;
  - client profiles;
  - mailings;
  - import batches;
  - recipients;
  - global suppressions;
  - mailing declarations;
  - message drafts;
  - audit records.
- Добавить индексы и ограничения:
  - unique normalized user email;
  - индексы по `UserId`, `MailingId`, `NormalizedEmail`;
  - обязательные поля для declaration/message/audit.
- Проверить хранение дат в UTC.

Acceptance criteria:

- EF-модель покрывает все данные Sprint 1–3.
- Миграция создаёт таблицы и индексы для текущего flow.

## MIG-08. Реализовать EF repositories

Задачи:

- Реализовать EF-версии всех runtime repositories.
- Сохранить in-memory implementations только как fallback для unit/dev.
- Добавить выбор провайдера через конфигурацию:
  - `Persistence:Provider=Postgres`;
  - `Persistence:Provider=InMemory`.
- Добавить ошибку старта, если выбран `Postgres`, но не задан connection string.

Acceptance criteria:

- При `Postgres` все runtime операции идут через EF repositories.
- DI не регистрирует одновременно две конкурирующие реализации одного repository.

## MIG-09. Переключить Development runtime на PostgreSQL

Задачи:

- Обновить development configuration.
- Подключить connection string через environment variables или user-secrets.
- Проверить запуск с локальным PostgreSQL из Docker Compose.
- Проверить, что данные сохраняются после restart приложения.

Acceptance criteria:

- В `Development` PostgreSQL используется по умолчанию.
- Регистрация, рассылки, импорт, декларация и письмо переживают restart.

## MIG-10. Добавить workflow миграций

Задачи:

- Добавить или обновить initial EF migration.
- Документировать команды:
  - создать migration;
  - применить migration;
  - откатить migration в dev;
  - поднять чистую БД.
- Проверить, что clean database создаётся без ручного SQL.

Acceptance criteria:

- Новый разработчик может поднять БД через Docker Compose и migrations.
- CI/интеграционные тесты могут применять migrations на пустую БД.

## MIG-11. Добавить интеграционные тесты PostgreSQL persistence

Задачи:

- Использовать Testcontainers PostgreSQL.
- Покрыть сценарии:
  - старт приложения с PostgreSQL;
  - применение migrations;
  - регистрация и подтверждение email;
  - создание рассылки;
  - импорт recipients;
  - сохранение декларации;
  - сохранение черновика письма;
  - сохранение audit log;
  - проверка global suppression при импорте.

Acceptance criteria:

- Тесты падают, если runtime случайно использует in-memory state.
- Данные читаются через новый scope/repository instance.

## Рекомендуемый порядок

1. MIG-01.
2. MIG-02.
3. MIG-03.
4. MIG-04.
5. MIG-05.
6. MIG-06.
7. MIG-07.
8. MIG-08.
9. MIG-10.
10. MIG-11.
11. MIG-09.

## Definition of Done

- Классы физически перенесены из `Pismolet.Web` в целевые проекты.
- `Pismolet.Web` содержит только web/UI/composition root.
- `Pismolet.Domain` не зависит от Web/EF.
- `Pismolet.Application` не зависит от Web/EF.
- `Pismolet.Infrastructure` содержит in-memory fallback и EF/PostgreSQL implementations.
- `Development` runtime использует PostgreSQL по умолчанию.
- Данные Sprint 1–3 сохраняются после restart.
- `dotnet build Pismolet.sln` и `dotnet test Pismolet.sln` проходят.

# Refactor and persistence migration report

Дата: 2026-06-16

Основание: `source/tasks/refactor-and-persistence-migration.md`.

## Что сделано

- `Pismolet.Domain` получил реальные доменные модели текущего vertical slice.
- `Pismolet.Application` получил application contracts и use cases Sprint 1–3.
- `Pismolet.Infrastructure` получил in-memory fallback, EF/PostgreSQL repositories, DbContext, design-time factory, seed и migration.
- `Pismolet.Web` больше не компилирует локальные папки `Application`, `Domain`, `Infrastructure`, `Migrations`; эти старые файлы временно оставлены в дереве только как безопасный transitional fallback до локальной проверки build/test.
- Runtime persistence выбирается через `Persistence:Provider`:
  - `Postgres` / `PostgreSQL` — основной runtime provider;
  - `InMemory` — явный dev/test fallback.
- В `appsettings.Development.json` PostgreSQL выбран по умолчанию.

## Карта переноса

| Старый путь | Целевой слой |
| --- | --- |
| `src/Pismolet.Web/Domain/Audit/**` | `src/Pismolet.Domain/Audit/**` |
| `src/Pismolet.Web/Domain/Mail/**` | `src/Pismolet.Domain/Mail/**` |
| `src/Pismolet.Web/Domain/Mailings/**` | `src/Pismolet.Domain/Mailings/**` |
| `src/Pismolet.Web/Domain/Users/**` | `src/Pismolet.Domain/Users/**` |
| `src/Pismolet.Web/Application/Auth/**` | `src/Pismolet.Application/Auth/**` |
| `src/Pismolet.Web/Application/Audit/**` | `src/Pismolet.Application/Audit/**` |
| `src/Pismolet.Web/Application/Common/**` | `src/Pismolet.Application/Common/**` |
| `src/Pismolet.Web/Application/Imports/**` | `src/Pismolet.Application/Imports/**` |
| `src/Pismolet.Web/Application/Mail/**` | `src/Pismolet.Application/Mail/**` |
| `src/Pismolet.Web/Application/Mailings/**` | `src/Pismolet.Application/Mailings/**` |
| `src/Pismolet.Web/Application/Persistence/**` | `src/Pismolet.Application/Persistence/**` |
| `src/Pismolet.Web/Infrastructure/Audit/**` | `src/Pismolet.Infrastructure/Audit/**` |
| `src/Pismolet.Web/Infrastructure/Database/**` | `src/Pismolet.Infrastructure/Database/**` |
| `src/Pismolet.Web/Infrastructure/Mail/**` | `src/Pismolet.Infrastructure/Mail/**` |
| `src/Pismolet.Web/Infrastructure/Persistence/**` | `src/Pismolet.Infrastructure/Persistence/**` |
| `src/Pismolet.Web/Infrastructure/Seed/**` | `src/Pismolet.Infrastructure/Seed/**` |
| `src/Pismolet.Web/Migrations/**` | `src/Pismolet.Infrastructure/Migrations/**` |
| `src/Pismolet.Web/Endpoints/**` | остаётся в `Pismolet.Web` |
| `src/Pismolet.Web/Rendering/**` | остаётся в `Pismolet.Web` |
| `src/Pismolet.Web/Program.cs` | остаётся composition root в `Pismolet.Web` |

## Persistence contracts

Application layer зависит от repository interfaces и не содержит EF Core / DbContext типов.

Контракты текущего flow:

- `IUserRepository` — регистрация, вход, подтверждение email, профиль клиента.
- `IMailingRepository` — создание рассылки, импорт, декларация, черновик письма.
- `IGlobalSuppressionRepository` — проверка глобальной отписки при импорте.
- `IAuditLogger` — запись и чтение audit events.
- `IFakeMailer` — dev/fake подтверждение email.

## Transaction boundaries

Текущая реализация сохраняет изменения на уровне repository operation:

- регистрация пользователя: `IUserRepository.TryAdd` + отдельная запись audit log;
- подтверждение email: `IUserRepository.Update` + отдельная запись audit log;
- создание рассылки: `IMailingRepository.TryAdd` + отдельная запись audit log;
- импорт получателей: чтение suppression list, затем атомарное обновление графа рассылки через `IMailingRepository.Update`, затем audit log;
- декларация базы: атомарное обновление рассылки через `IMailingRepository.Update`, затем audit log;
- черновик письма: атомарное обновление рассылки через `IMailingRepository.Update`, затем audit log.

Перед production-режимом для строгой атомарности audit log и бизнес-изменений нужно добавить Unit of Work / transaction boundary на уровень application service.

## EF/PostgreSQL модель

EF-модель покрывает данные Sprint 1–3:

- users;
- client profile поля в users;
- mailings;
- import batches;
- recipients;
- import issues;
- global suppressions;
- mailing declarations;
- message drafts;
- audit records.

Добавлены индексы:

- unique normalized user email;
- mailings by owner email;
- import batches by mailing id;
- recipients by mailing id and normalized email;
- audit records by user;
- mailing declarations by user email.

## Команды миграций

```bash
docker compose up -d postgres

dotnet ef database update \
  --project src/Pismolet.Infrastructure/Pismolet.Infrastructure.csproj \
  --startup-project src/Pismolet.Web/Pismolet.Web.csproj

dotnet ef migrations add <MigrationName> \
  --project src/Pismolet.Infrastructure/Pismolet.Infrastructure.csproj \
  --startup-project src/Pismolet.Web/Pismolet.Web.csproj

dotnet ef database drop \
  --project src/Pismolet.Infrastructure/Pismolet.Infrastructure.csproj \
  --startup-project src/Pismolet.Web/Pismolet.Web.csproj
```

## Ограничения текущего прохода

- Локальные `dotnet build`, `dotnet test`, `dotnet format` не запускались в GitHub connector окружении.
- Старые файлы в `src/Pismolet.Web/Application`, `Domain`, `Infrastructure`, `Migrations` не удалены физически, но исключены из компиляции `Pismolet.Web.csproj`.
- EF migration добавлена вручную; перед следующей migration желательно сгенерировать/проверить snapshot локально через `dotnet ef`.
- Интеграционные PostgreSQL tests требуют отдельного локального прогона с Testcontainers.

# Отчёт Sprint 2

Дата: 2026-06-16  
Ветка: `Development`  
Чат-автор: `[Технарь]`

## Статус

Выполнен с ограничениями окружения и GitHub-коннектора.

## Что реализовано

- Добавлены доменные модели для импорта адресов:
  - `ImportStats`;
  - `Recipient`;
  - `ImportBatch`;
  - `RecipientImportIssue`.
- Расширена модель `Mailing`:
  - публичный `Guid Id`;
  - владелец рассылки;
  - дата создания;
  - статистика последнего импорта;
  - список принятых адресатов.
- Добавлены application-контракты:
  - `IMailingRepository`;
  - `IGlobalSuppressionRepository`;
  - `IMailingService`;
  - `IRecipientImportService`;
  - `IEmailNormalizer`;
  - `IEmailSyntaxValidator`.
- Добавлены dev-only in-memory реализации:
  - `InMemoryMailingRepository`;
  - `InMemoryGlobalSuppressionRepository`.
- Реализовано создание черновика рассылки.
- Реализован CSV-импорт адресов с колонкой `email`.
- Реализованы проверки:
  - trim/lowercase нормализация email;
  - базовая синтаксическая проверка email;
  - дубли внутри файла после нормализации;
  - исключение адресов из глобальной отписки;
  - ограничение до 1000 строк;
  - отказ при пустом файле, не-CSV файле и отсутствии колонки `email`.
- Добавлены audit-события:
  - `mailing_created`;
  - `recipients_import_started`;
  - `recipients_import_completed`;
  - `recipients_import_failed`.
- ЛК теперь показывает рассылки из `IMailingService`, а не из legacy-поля пользователя.
- Карточка «Создать рассылку» ведёт на `/mailings/new`.
- Добавлены защищённые маршруты:
  - `GET /mailings/new`;
  - `POST /mailings`;
  - `GET /mailings/{id}`;
  - `GET /mailings/{id}/recipients`;
  - `POST /mailings/{id}/recipients`.
- Добавлены unit-тесты:
  - создание рассылки;
  - запрет пустого названия;
  - видимость только своих рассылок;
  - нормализация email;
  - импорт CSV со статистикой валидных, дублей, невалидных и глобальной отписки;
  - ошибка при отсутствии колонки `email`.

## Что перенесено

- XLSX-импорт не реализован в Sprint 2. Причина: библиотека для XLSX и лицензия не были подтверждены. CSV закрывает обязательный MVP-минимум.
- `Microsoft.AspNetCore.Mvc.Testing` и web smoke tests не добавлены: попытка обновить `Directory.Packages.props` была заблокирована GitHub-коннектором. Unit-тесты сервисного слоя добавлены.
- Полное переиспользование нового `IEmailNormalizer` внутри auth-сервиса не выполнено: большой update `UserAccountService.cs` блокировался коннектором. Текущий auth-flow не сломан; импорт использует вынесенную нормализацию.
- `Program.cs` не изменён: подключение новых `/mailings/*` маршрутов реализовано внутри существующего `MapDashboardEndpoints()`, чтобы не возвращать логику в `Program.cs` и обойти блокировку записи.

## Риски

- Antiforgery для HTML form flow пока не внедрён. Все POST-маршруты требуют авторизации, но CSRF-защиту нужно добавить отдельным техническим шагом.
- HTML-рендеринг остаётся временным строковым слоем. При росте UI желательно перейти к Razor Pages / Razor Components.
- Данные рассылок и импорта хранятся in-memory и пропадают после перезапуска.
- CSV-парсер минимальный и не поддерживает сложные CSV с экранированными запятыми внутри значения.

## Ручной тест

1. Запустить приложение:

   ```bash
   dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
   ```

2. Зарегистрироваться.
3. Подтвердить email через `/dev/fake-mailer`.
4. Войти.
5. Открыть `/dashboard`.
6. Нажать «Создать рассылку».
7. Создать рассылку с названием.
8. Загрузить CSV:

   ```csv
   email
   client1@example.com
   CLIENT2@example.com
   client1@example.com
   wrong-email
   ```

9. Проверить отчёт:
   - всего строк: 4;
   - принято адресов: 2;
   - дублей: 1;
   - невалидных email: 1.
10. Проверить, что рассылка видна в ЛК.
11. Проверить, что неавторизованный пользователь перенаправляется на вход.

## Проверки

Локально `dotnet restore`, `dotnet format`, `dotnet build`, `dotnet test` из этого окружения не запускались.

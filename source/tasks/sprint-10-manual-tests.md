# Sprint 10 — ручные проверки админки, лимитов, блокировок и audit log

## Предусловия

- Ветка: `Development`.
- В конфигурации задан admin allowlist:
  - `Admin:AllowedEmails` или `PISMOLET_ADMIN_EMAILS`.
- Приложение запущено в dev/smoke окружении.
- Есть минимум один обычный клиент и одна рассылка, прошедшая до статуса `Одобрено`.

## Smoke-flow

1. Войти обычным пользователем и открыть `/admin`.
   - Ожидаемо: редирект на логин или отказ доступа.
   - Нельзя выполнить POST actions `/admin/*` обычным пользователем.

2. Войти администратором и открыть `/admin`.
   - Ожидаемо: dashboard со счётчиками клиентов, рассылок, жалоб, bounce, audit log.

3. Открыть `/admin/clients`.
   - Проверить список клиентов, статус, дневной лимит, признак премодерации.

4. Изменить дневной лимит клиента.
   - Проверить, что в `/admin/audit` появилось `admin_client_daily_limit_changed`.
   - Запустить рассылку клиента и убедиться, что отправка учитывает новый лимит.

5. Заблокировать клиента.
   - Проверить статус клиента в `/admin/clients` и `/admin/users/{email}`.
   - Проверить audit action `admin_client_blocked`.
   - Попробовать оплатить или запустить новую рассылку клиента.
   - Ожидаемо: backend возвращает отказ на русском языке.

6. Разблокировать клиента.
   - Проверить audit action `admin_client_unblocked`.
   - Проверить, что клиент может продолжить flow, если нет других ограничений.

7. Включить обязательную премодерацию клиента.
   - Проверить audit action `admin_client_premoderation_changed`.
   - Создать следующую рассылку клиента и запустить проверку.
   - Ожидаемо: risk-flow добавляет forced premoderation и рассылка попадает на ручную проверку.

8. Открыть `/admin/settings/mvp`.
   - Изменить цену письма, default дневной лимит и премодерацию новых клиентов.
   - Проверить audit action `admin_mvp_settings_changed`.
   - Создать нового клиента и проверить применение default-настроек.
   - Создать новую оплату и проверить новую цену.

9. Заблокировать рассылку через admin POST action `/admin/mailings/{id}/block`.
   - Проверить audit action `admin_mailing_blocked`.
   - Попробовать запуск/повтор отправки.
   - Ожидаемо: заблокированная рассылка не отправляется.

10. Открыть разделы:
    - `/admin/imports`;
    - `/admin/complaints`;
    - `/admin/delivery-errors`;
    - `/admin/replies`;
    - `/admin/audit`.

11. Проверить, что страницы не показывают:
    - raw provider payload;
    - unsubscribe token;
    - reply token;
    - секреты провайдера;
    - полные тела писем/ответов.

## Команды проверки

```bash
dotnet build Pismolet.sln
dotnet test Pismolet.sln
```

## Что смотреть при smoke test

- Все `/admin/*` routes требуют `AdminOnly`.
- Admin actions пишут audit log.
- Новая цена применяется только к новым payment attempts.
- Блокировка клиента не является отдельным `IsBlocked`, а идёт через `ClientProfile.Status`.
- Блокировка рассылки идёт через `MailingStatus.Blocked`.
- Пользовательские тексты на русском языке.

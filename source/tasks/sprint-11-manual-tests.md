# Sprint 11 — ручной smoke checklist

## Dev / in-memory full e2e

1. Запустить приложение в `Development` с `Persistence:Provider=InMemory`.
2. Открыть `/health` и убедиться, что статус `ok`.
3. Войти demo-пользователем `demo@pismolet.local` / `password123` или зарегистрировать нового пользователя.
4. Если регистрируется новый пользователь, подтвердить email через `/dev/fake-mailer`.
5. Проверить `/dashboard` и создать новую рассылку через `/mailings/new`.
6. Загрузить `docs/examples/demo_recipients.csv` на `/mailings/{id}/recipients`.
7. Проверить агрегированную статистику импорта: accepted, invalid, duplicate, global suppression.
8. Подтвердить базу на `/mailings/{id}/declaration`.
9. Заполнить письмо на `/mailings/{id}/message`.
10. Проверить preview: виден `/unsubscribe/example-token`, но не реальный token.
11. Перейти на `/mailings/{id}/payment`, создать fake payment и подтвердить fake success.
12. Запустить `/mailings/{id}/checks`.
13. Если рассылка попала на ручную модерацию, одобрить через `/admin/moderation`.
14. Запустить отправку на `/mailings/{id}/send`.
15. Проверить `/dev/fake-mailer`: есть отправленные fake письма, unsubscribe link и reply identity.
16. Через `/dev/webhooks/fake` отправить `delivered` и проверить delivery summary.
17. Повторить тот же webhook event id и убедиться, что счётчики не удвоились.
18. Отправить `hard_bounce` и проверить client suppression при следующем импорте.
19. Отправить `complaint` и проверить global suppression при следующем импорте.
20. Открыть unsubscribe link из fake письма и подтвердить отписку.
21. Повторить unsubscribe POST и убедиться в идемпотентности.
22. Через `/dev/replies/fake` создать inbound reply.
23. Проверить счётчик ответов на `/mailings/{id}` и `/mailings/{id}/send`.
24. Открыть `/admin/audit` и убедиться, что есть события по основному flow.
25. Проверить мобильную ширину главных страниц: dashboard, recipients, declaration, message, payment, checks, send, unsubscribe, admin.

## Негативные сценарии

1. Загрузить пустой CSV — ожидается «Файл пустой.».
2. Загрузить CSV без колонки `email` — ожидается «В файле должна быть колонка email.».
3. Загрузить CSV только с невалидными адресами — нельзя продолжить к оплате/отправке без accepted recipients.
4. Открыть `/mailings/{id}/send` до оплаты и одобрения — отправка недоступна.
5. Повторить fake payment callback — повторного списания/нового платежа быть не должно.
6. Повторить send job/refresh `/send` — уже обработанные получатели не дублируются.
7. Повторить webhook с тем же `providerEventId` — delivery summary не удваивается.
8. Заблокировать клиента в `/admin/clients` и проверить, что payment/send backend отказывает.
9. Заблокировать рассылку и проверить, что отправка не продолжается.
10. Проверить, что `/dev/*` endpoints недоступны вне `Development`.

## Postgres + Hangfire smoke

1. Поднять PostgreSQL.
2. Запустить приложение с `Persistence:Provider=Postgres` и `Sending:Queue=Hangfire`.
3. Пройти flow до отправки.
4. Проверить таблицы `send_events`, `provider_webhook_events`, `client_suppressions`, `global_suppressions`, `reply_events`.
5. Перезапустить приложение.
6. Проверить, что delivery/send/reply статистика сохранилась.
7. Проверить служебные таблицы Hangfire в схеме `hangfire`.

## Команды локальной проверки

```bash
dotnet restore Pismolet.sln
dotnet format Pismolet.sln --verify-no-changes
dotnet build Pismolet.sln
dotnet test Pismolet.sln
```

# MVP demo checklist

Этот checklist фиксирует основной демонстрационный сценарий Sprint 11. Он рассчитан на локальный запуск без реальных платежей и реальных email-отправок.

## Режим A: dev / in-memory full e2e

### Подготовка

1. Запустить приложение в `Development` с `Persistence:Provider=InMemory` и `MailProvider=FakeMailer`.
2. Убедиться, что dev endpoints доступны: `/dev/fake-mailer`, `/dev/webhooks/fake`, `/dev/replies/fake`.
3. Для admin-сценариев задать `Admin:AllowedEmails` или `PISMOLET_ADMIN_EMAILS` на email тестового администратора.

Ожидаемый результат: `/health` возвращает `ok`, главная страница открывается, реальные внешние провайдеры не используются.

### Основной happy path

| Шаг | URL | Действие | Ожидаемый результат |
| --- | --- | --- | --- |
| 1 | `/account/register` | Зарегистрировать пользователя `demo-client@example.test` | Письмо подтверждения уходит в fake mailer |
| 2 | `/dev/fake-mailer` | Открыть ссылку подтверждения email | Пользователь подтверждён и может войти |
| 3 | `/account/login` | Войти | Редирект в `/dashboard` |
| 4 | `/profile` | Создать или проверить профиль клиента | Видны лимит и статус клиента |
| 5 | `/mailings/new` | Создать рассылку | Редирект на загрузку адресов |
| 6 | `/mailings/{id}/recipients` | Загрузить `docs/examples/demo_recipients.csv` | Показана агрегированная статистика: принятые, ошибки, исключённые |
| 7 | `/mailings/{id}/declaration` | Подтвердить правомерность базы | Следующий шаг — редактор письма |
| 8 | `/mailings/{id}/message` | Заполнить отправителя, тему и текст | Preview показывает служебный блок и `/unsubscribe/example-token` |
| 9 | `/mailings/{id}/payment` | Проверить стоимость | Учитываются только принятые адреса |
| 10 | `/mailings/{id}/payment/fake-start` → fake success | Пройти fake-оплату | Статус становится «Оплачено» |
| 11 | `/mailings/{id}/checks` | Запустить проверку | Автоодобрение или понятная ручная модерация |
| 12 | `/admin/moderation` | Если нужна ручная проверка, одобрить | Рассылка получает статус «Одобрено» |
| 13 | `/mailings/{id}/send` | Запустить отправку | Появляются send events и безопасная статистика |
| 14 | `/dev/fake-mailer` | Найти отправленные письма | В письмах есть unsubscribe link и reply identity |
| 15 | `/dev/webhooks/fake` | Отправить `delivered` для одного provider message id | Блок «Доставка» обновляется без дублей |
| 16 | `/dev/webhooks/fake` | Отправить `hard_bounce` | Email попадает в client-level suppression |
| 17 | `/dev/webhooks/fake` | Отправить `complaint` | Email попадает в global suppression |
| 18 | `/unsubscribe/{token}` | Открыть ссылку из fake письма и подтвердить | Повторная отписка идемпотентна |
| 19 | `/dev/replies/fake` | Сформировать fake inbound reply | Ответ переслан клиенту через fake mailer, счётчик ответов растёт |
| 20 | `/admin/audit` | Проверить audit | Видны события оплаты, проверки, отправки, webhook, отписки и admin actions |

### Что делать, если шаг не прошёл

- Нет письма подтверждения: проверить `/dev/fake-mailer`, `MailProvider=FakeMailer`, окружение `Development`.
- Нет доступа в `/admin`: проверить `Admin:AllowedEmails` или `PISMOLET_ADMIN_EMAILS` и email текущего пользователя.
- Оплата не запускается: проверить, что импорт, декларация и письмо завершены.
- Проверка не запускается: проверить статус оплаты и блокировку клиента/рассылки.
- Отправка не запускается: проверить, что рассылка одобрена, клиент не заблокирован, есть принятые адреса.
- Webhook отклоняется: проверить `X-Pismolet-Webhook-Secret` и настройку `Webhooks:FakeProviderSecret`.
- Unsubscribe token недействителен: взять свежую ссылку из fake письма, а не из preview.
- Reply не сопоставлен: использовать fake reply из `/dev/replies/fake`, созданный на основе отправленного fake письма.

## Режим B: Postgres + Hangfire smoke

Этот режим проверяет устойчивые storage/queue части, но не требует полной production persistence для payment/risk/moderation/settings.

1. Поднять PostgreSQL.
2. Задать `Persistence:Provider=Postgres` и `ConnectionStrings:PismoletDb`.
3. Задать `Sending:Queue=Hangfire`.
4. Запустить приложение в `Development`, чтобы применились миграции.
5. Пройти happy path до отправки.
6. Убедиться, что созданы таблицы `send_events`, `provider_webhook_events`, `client_suppressions`, `global_suppressions`, `reply_events`.
7. Перезапустить приложение.
8. Открыть `/mailings/{id}/send` и проверить, что статистика доставки сохранилась.
9. Отправить повторный webhook с тем же `providerEventId` и убедиться, что счётчики не удвоились.
10. Проверить, что Hangfire создал служебные таблицы в схеме `hangfire`.

## Критерии успешной демонстрации

- Основной flow проходится кнопками и ссылками, без ручного SQL.
- Ошибки показываются на русском языке.
- Обычный пользователь не видит provider payload, raw body, unsubscribe/reply tokens и секреты.
- Dev-only endpoints доступны только в `Development`.
- Smoke flow не создаёт реальных платежей и реальных email-отправок.

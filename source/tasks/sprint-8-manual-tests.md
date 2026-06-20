# Sprint 8 — ручные проверки webhooks

## Подготовка

1. Запустить приложение в `Development`.
2. Убедиться, что задан `Webhooks:FakeProviderSecret` или используется dev default `dev-fake-webhook-secret`.
3. Пройти MVP-flow до отправки рассылки через fake provider.
4. Открыть `/mailings/{id}/send` и убедиться, что есть отправленные fake-события.

## Delivery events

1. Открыть `/dev/webhooks/fake`.
2. Вставить `ProviderMessageId` отправленного письма.
3. Выбрать `delivered`.
4. Отправить событие.
5. Открыть `/mailings/{id}/send`.
6. Проверить, что счётчик «Доставлено» увеличился на 1.
7. Повторить тот же `ProviderEventId`.
8. Проверить, что счётчик не увеличился повторно.

## Soft bounce

1. Через `/dev/webhooks/fake` отправить `soft_bounce`.
2. Проверить, что на странице отправки увеличился счётчик «Временная ошибка».
3. Проверить, что email не попал в global suppression и client suppression.

## Hard bounce

1. Через `/dev/webhooks/fake` отправить `hard_bounce`.
2. Проверить, что на странице отправки увеличился счётчик «Постоянная ошибка».
3. Проверить в БД/отладке, что создана запись `client_suppressions`.
4. Создать следующую рассылку тем же клиентом с этим email.
5. Проверить, что email исключается как «Исключено из-за ошибки доставки».
6. Создать рассылку другим клиентом с тем же email.
7. Проверить, что другой клиент не блокируется этим hard bounce.

## Complaint

1. Через `/dev/webhooks/fake` отправить `complaint`.
2. Проверить, что на странице отправки увеличился счётчик «Жалоба».
3. Проверить, что email попал в `global_suppressions`.
4. Создать рассылку любым клиентом с этим email.
5. Проверить, что email исключается как глобально отписанный.
6. Повторить complaint с тем же `ProviderEventId` и убедиться, что дублей нет.

## Unknown/unmatched

1. Отправить webhook с `eventType=unknown`.
2. Проверить, что endpoint возвращает безопасный OK/processed, а delivery status конкретного письма не меняется.
3. Отправить webhook с несуществующим `ProviderMessageId`.
4. Проверить, что событие сохранено как unmatched/ignored, но новый получатель или рассылка не создаётся.

## Защита endpoint

1. Отправить `POST /webhooks/email/fake` без `X-Pismolet-Webhook-Secret`.
2. Проверить `403`.
3. Отправить некорректный JSON.
4. Проверить безопасный `400` без stack trace и внутренних данных.

## Persistence

1. Запустить приложение с `Persistence:Provider=Postgres`.
2. Применить миграции.
3. Отправить delivered/hard_bounce/complaint webhook.
4. Перезапустить приложение.
5. Проверить, что `provider_webhook_events`, delivery summary и suppression/blocklist сохранились.
# Sprint 9 — ручные проверки

1. Запустить dev-приложение с `Persistence:Provider=InMemory` или Postgres.
2. Создать рассылку и пройти flow до отправки через fake provider.
3. Открыть `/dev/fake-mailer` или `/dev/replies/fake`.
4. Убедиться, что у фактически отправленного fake-письма есть `ReplyToAddress`, `ReplyToken` и `ProviderMessageId`.
5. На `/dev/replies/fake` создать fake reply на базе отправленного письма.
6. Проверить, что inbound reply принят и появился в списке последних `ReplyEvent`.
7. Открыть `/mailings/{id}/send` и проверить блок «Ответы получателей».
8. Проверить, что клиенту создана fake-пересылка ответа, помеченная как forwarded reply.
9. Повторить тот же inbound event и убедиться, что счётчик ответов не увеличился.
10. Отправить payload с заголовком `Auto-Submitted: auto-replied` и убедиться, что ответ сохранён как auto-reply и не переслан клиенту.
11. Отправить inbound payload с испорченным `replyToken` и убедиться, что событие сохранено как unmatched без создания рассылки/получателя.
12. Запустить cleanup через `/dev/replies/cleanup` и убедиться, что технический `ReplyEvent` остался, а тело ответа удаляется после TTL.
13. В Postgres-сценарии перезапустить приложение и убедиться, что `reply_events` и счётчик ответов сохранились.

Проверки безопасности:

- публичный inbound endpoint требует `X-Pismolet-Webhook-Secret`;
- ordinary client UI не показывает raw payload, provider ids и внутренние tokens;
- unsubscribe token с purpose `global_unsubscribe` не принимается как reply token;
- автоответчики и mail loop не пересылаются клиенту.

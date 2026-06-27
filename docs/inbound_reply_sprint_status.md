# Статусы спринтов этапа 3: пересылка ответов получателей

Дата обновления: 2026-06-27

Источник плана: `docs/production_readiness_plan.md`, раздел `3.14. Спринты и подэтапы реализации`.

## Сводка

| Спринт | Статус | Комментарий |
|---|---|---|
| 3.0. Инвентаризация текущего reply-контура | Завершён | Создан `docs/inbound_reply_inventory.md`; зафиксированы существующие модели, сервисы, endpoints, queue, token и ограничения. |
| 3.1. Конфигурация inbound reply и безопасный skeleton | Завершён | Добавлены `InboundReplySpoolOptions`, выключенный по умолчанию hosted service skeleton и регистрация вне Testing. |
| 3.2. MIME parser и token extraction | Частично завершён | Добавлены `IInboundReplyMimeParser`, `InboundReplyRawMessage`, базовый MimeKit parser. Полный token extraction из envelope/headers остаётся открытым подпунктом. |
| 3.3. Auto-reply, bounce и дедупликация | Частично начат | Добавлен отдельный `InboundReplyAutoReplyDetector`. Подключение detector к `InboundReplyProcessingService` ещё не выполнено. |
| 3.4. Подключение processing service и очереди пересылки | Частично покрыт текущей архитектурой | Existing `InboundReplyProcessingService` уже связывает inbound event с matching/queue/forward. Отдельный synthetic integration test ещё не добавлен. |
| 3.5. Spool reader и файловая обработка | Завершён для MVP-каркаса | Reader обрабатывает `incoming/*.eml`, двигает файлы в `processing`, вызывает parser/processor, переносит в `processed` или `failed`, пишет `.error`, чистит retention. |
| 3.6. Админ-диагностика и отчёт клиента | Не начат | Следующий спринт. |
| 3.7. Инфраструктурный runbook и server dry-run | Не начат | Нужно подготовить runbook и dry-run сценарий. |
| 3.8. Production smoke и включение reply-домена | Не начат | Выполнять только после runbook и серверной настройки. |
| 3.9. Юридическая и retention-синхронизация | Не начат | Выполнять после финального фактического поведения retention. |

## Открытые технические подпункты

1. Подключить `InboundReplyAutoReplyDetector` в `InboundReplyProcessingService` вместо локального минимального `IsAutoReply`.
2. Расширить token extraction для `.eml`:
   - envelope recipient;
   - `X-Original-To`;
   - `Delivered-To`;
   - `To`;
   - `Cc` fallback;
   - форматы `reply+<token>@reply.pismolet.ru` и `<token>@reply.pismolet.ru`.
3. Передать envelope recipient в `InboundReplyRawMessage`, когда будет выбран Postfix pipe/sidecar-контракт.
4. Добавить тесты parser/spool/auto-reply.
5. Добавить admin diagnostics для reply events.
6. Подготовить server runbook.

## Последние commit SHA по этапу

- `8fd0130087e71c6f0cfd7c8ffc8a09c588d3df50` — инвентаризация reply-контура.
- `6b3def1fe0e6dae5cc344c6c1cc9c49c180f0c26` — настройки spool.
- `6fb88a2bb3025215863e3dc7dcd34b66226d0c80` — skeleton spool reader.
- `768350357bca27d3968e0011e5fdc39115a35904` — чтение настроек inbound spool.
- `208d03fd2ec0aa8f5c781b40acc15a6c0780cf89` — контракт MIME parser.
- `0c5fc9d0fcfc5883eb46e733a6c505936945664c` — базовый MimeKit parser.
- `317b9d71c31fa5031d0d622ac2a15725ac3dce23` — detector системных inbound ответов.
- `06f5f269fe6faa96e2cea3f9865624a20370840f` — обработка `.eml` из spool.
- `b14a3e8fe83f2315a7c767c355611a33819ce190` — регистрация parser inbound spool.
- `c25e02a2c71a9d845718a1ad4eb75582e9ceb0af` — retention cleanup spool.

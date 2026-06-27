# Статусы спринтов этапа 3: пересылка ответов получателей

Дата обновления: 2026-06-27

Источник плана: `docs/production_readiness_plan.md`, раздел `3.14. Спринты и подэтапы реализации`.

## Сводка

| Спринт | Статус | Комментарий |
|---|---|---|
| 3.0. Инвентаризация текущего reply-контура | Завершён | Создан `docs/inbound_reply_inventory.md`; зафиксированы существующие модели, сервисы, endpoints, queue, token и ограничения. |
| 3.1. Конфигурация inbound reply и безопасный skeleton | Завершён | Добавлены `InboundReplySpoolOptions`, выключенный по умолчанию hosted service skeleton и регистрация вне Testing. |
| 3.2. MIME parser и token extraction | Завершён для MVP | Parser извлекает token из envelope, X-Original-To, Delivered-To, To и Cc fallback; добавлены unit-тесты parser. |
| 3.3. Auto-reply, bounce и дедупликация | Завершён для MVP | Подключён `InboundReplyAutoReplyDetector`; добавлены unit-тесты detector-а; дедупликация по provider event уже есть в processing service. |
| 3.4. Подключение processing service и очереди пересылки | Частично покрыт текущей архитектурой | Existing `InboundReplyProcessingService` уже связывает inbound event с matching/queue/forward. Отдельный synthetic integration test ещё не добавлен. |
| 3.5. Spool reader и файловая обработка | Завершён для MVP-каркаса | Reader обрабатывает eml из incoming, двигает файлы в processing, вызывает parser/processor, переносит в processed или failed, пишет error-файл, чистит retention. |
| 3.6. Админ-диагностика и отчёт клиента | Завершён для MVP | Используется существующий `/admin/replies`; страница улучшена: маскирует email, показывает статус, рассылку, тему, body status и ошибку без body/raw/token. |
| 3.7. Инфраструктурный runbook и server dry-run | Завершён для документации | Создан `docs/inbound_reply_runbook.md`; server dry-run выполнять после deploy. |
| 3.8. Production smoke и включение reply-домена | Не начат | Выполнять только после runbook и серверной настройки. |
| 3.9. Юридическая и retention-синхронизация | Не начат | Выполнять после финального фактического поведения retention. |

## Открытые технические подпункты

1. Передать фактический envelope recipient в `InboundReplyRawMessage`, когда будет выбран Postfix pipe/sidecar-контракт.
2. Добавить integration-тест spool reader -> parser -> processing service.
3. Выполнить server dry-run по runbook.
4. После dry-run обновить фактический статус спринта 3.8.

## Последние commit SHA по этапу

- `8fd0130087e71c6f0cfd7c8ffc8a09c588d3df50` — инвентаризация reply-контура.
- `6b3def1fe0e6dae5cc344c6c1cc9c49c180f0c26` — настройки spool.
- `6fb88a2bb3025215863e3dc7dcd34b66226d0c80` — skeleton spool reader.
- `768350357bca27d3968e0011e5fdc39115a35904` — чтение настроек inbound spool.
- `208d03fd2ec0aa8f5c781b40acc15a6c0780cf89` — контракт MIME parser.
- `0c5fc9d0fcfc5883eb46e733a6c505936945664c` — базовый MimeKit parser.
- `317b9d71c31fa5031d0d622ac2a15725ac3dce23` — detector системных inbound ответов.
- `06f5f269fe6faa96e2cea3f9865624a20370840f` — обработка eml из spool.
- `b14a3e8fe83f2315a7c767c355611a33819ce190` — регистрация parser inbound spool.
- `c25e02a2c71a9d845718a1ad4eb75582e9ceb0af` — retention cleanup spool.
- `5d47d83722d8231569d70107232ea4eead2c00f9` — admin endpoint диагностики ответов.
- `d322018838d7af1ce940f547058cf8d882706827` — inbound reply runbook.
- `4c7b16596551589afb15ac935d5b52dccfd8b912` — подключение admin route.
- `a9ee303089c2601c243795319646d47d0ca5ef15` — подключение detector и fallback token extraction.
- `63acf9b9ba5ea61917db49676c8b5ad1c86284eb` — token extraction в MIME parser.
- `6b6f5e81eb41f119680d5ceac731b5b19db28831` — совместимая проверка символов parser.
- `b6202466ffbecb8cba4c07746fb6c5b7560447bb` — unit-тесты MIME parser.
- `4ac6ab429766dcc4f8f9363b7e451b6bd1b03afe` — совместимая проверка local-part matching.
- `f535ab8180c0542db71b0a768824b7f88d2c0c32` — unit-тесты auto-reply detector.
- `b11efcae36a547e7f067ae77b8cf705bdae7ec3a` — admin UI тест ответов.
- `f7f4473bfe54bbf15e7e25f79d46dd1c45f3f9e9` — совместимая проверка raw MIME в admin тесте.
- `be73ef763367ee9d22fb0c781caf63b6ddffeb71` — отключение дублирующего admin replies route.
- `8b139fcc86781ee2313e40bb760f26a6e650e99d` — тест существующей страницы `/admin/replies`.
- `20747807d69d3ab40301e349cfa3ef319064c2af` — улучшение admin диагностики `/admin/replies`.
- `cc56e5d524f32c32f906d6844d694ebaf89a1642` — ослабление проверки служебного текста admin replies.

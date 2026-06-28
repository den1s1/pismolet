# Production readiness: этап 3 — ответы получателей через reply alias

Дата: 2026-06-28

Статус: этап 3 по production-пути входящих ответов закрыт для MVP. Build/test зелёные, production smoke пройден.

Связанные документы:

- `docs/production_readiness_plan.md` — исходный план этапа 3.
- `docs/inbound_reply_alias_plan.md` — детальный план и факт реализации alias-модели.
- `docs/inbound_reply_alias_production_smoke.md` — smoke checklist reply alias.
- `docs/inbound_reply_runbook.md` — эксплуатационный runbook, если требуется дополнительная серверная детализация.

## Итоговое решение

Видимый `Reply-To` больше не содержит длинный технический token. Для каждого клиента используется стабильный alias на отдельном reply-домене.

Фактический production-пример:

```text
den1s@reply.pismolet.ru
```

Сопоставление обычных ответов выполняется не по token в видимом адресе, а по техническим заголовкам `In-Reply-To` / `References` и сохранённому outbound message mapping.

## Что включено

- `reply.pismolet.ru` принимает входящую почту через MX/Postfix.
- Postfix складывает `.eml` в spool `/var/lib/pismolet/inbound-replies`.
- Приложение читает spool, парсит MIME и создаёт `ReplyEvent`.
- При исходящей отправке создаётся клиентский alias и outbound message mapping.
- Обычный Reply сопоставляется с рассылкой и получателем.
- Matched reply пересылается владельцу рассылки.
- Known-alias fallback пересылается владельцу alias, если alias известен, но mapping не найден.
- Unknown alias не пересылается клиентам и остаётся только в admin diagnostics.
- Auto-reply / mail loop не пересылается.
- `/admin/replies` показывает alias, flow, статусы, полные email-адреса и диагностические причины.
- Raw MIME, reply token и тело письма в админке не показываются.

## Production smoke — подтверждено

Проверено на production:

1. Реальная тестовая рассылка создаёт alias `den1s` и outbound message mappings.
2. `Reply-To` в письме: `den1s@reply.pismolet.ru`.
3. Длинного `reply+v1...` token в `Reply-To` нет.
4. Обычный Reply от внешнего адреса сопоставляется с рассылкой и получателем.
5. Ответ пересылается владельцу рассылки.
6. Новое письмо напрямую на известный alias без `In-Reply-To` / `References` проходит как known-alias fallback.
7. Known-alias fallback сохраняет диагностику после пересылки:
   - `ProcessingStatus = Forwarded`;
   - `ClientId = den1s@mail.ru`;
   - `ForwardToEmailNormalized = den1s@mail.ru`;
   - `ErrorCode = reply_message_reference_missing`.
8. Unknown alias проходит как unmatched:
   - `ProcessingStatus = Unmatched`;
   - `ErrorCode = reply_alias_unknown`;
   - пересылки клиенту нет.
9. Auto-reply проходит как ignored:
   - `ProcessingStatus = IgnoredAutoReply`;
   - `ErrorCode = auto_reply`;
   - пересылки нет.
10. Временный DB trigger для сохранения fallback diagnostics удалён; сохранение диагностики подтверждено кодом.
11. Маскирование email в `/admin/replies` убрано.

## Обновление acceptance criteria этапа 3

Для MVP выполнено:

- получатель отвечает обычной кнопкой Reply;
- входящий ответ приходит на `reply.pismolet.ru`;
- приложение сопоставляет ответ с исходящим письмом по message id mapping;
- приложение создаёт `ReplyEvent`;
- matched reply пересылается владельцу рассылки;
- known-alias fallback не теряет письмо;
- unknown alias не пересылается случайному клиенту;
- auto-reply и loop-защита проверены;
- в ЛК нет полноценного inbox;
- в admin diagnostics есть безопасная наблюдаемость без raw MIME и тела письма;
- build/test зелёные.

## Отличие от раннего плана этапа 3

Ранний план допускал token-aware `Reply-To` вида `reply+token@reply.pismolet.ru`. По результатам реальной проверки это признано непригодным для production UX: адрес выглядел подозрительно и мог не приниматься почтовым клиентом.

Финальная MVP-модель заменяет видимый token на клиентский alias и переносит сопоставление в message id mapping.

## Остаточные задачи после закрытия этапа 3

Не блокируют MVP-ready статус reply alias:

- при необходимости расширить runbook процедурами replay failed `.eml` и аварийного выключения worker;
- отдельно решить вопрос вложений во входящих ответах;
- отдельно решить вопрос фильтров/поиска в `/admin/replies`, если объём reply events вырастет;
- синхронизировать `docs/specification.md`, раздел A, с продуктовой моделью alias-ответов;
- синхронизировать `docs/specification.md`, раздел C, если юридическая модель хранения/retention ответов будет уточняться.

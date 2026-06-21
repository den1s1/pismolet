# MVP acceptance

Документ фиксирует критерии приёмки MVP Письмолёта на Sprint 11. Он не добавляет новые production-обязательства; новые требования после Sprint 11 должны попадать в post-MVP или production hardening.

## Статусы критериев

- **Реализовано** — есть рабочий MVP-flow или dev/prod-compatible срез.
- **Частично реализовано** — достаточно для локального demo, но есть ограничения.
- **Production hardening** — нужно перед production, но не блокирует MVP-testing.
- **Post-MVP** — не входит в согласованный MVP.

## Продуктовые критерии MVP

| Критерий | Статус | Комментарий |
| --- | --- | --- |
| Простая рассылка без BCC | Реализовано | Рассылка создаётся и отправляется через сервисный fake provider. |
| Импорт адресов CSV | Реализовано | CSV с колонкой `email`, ручная вставка, агрегированная статистика. |
| Импорт XLSX | Частично реализовано | Поддержка есть через ClosedXML; требуется smoke на реальном файле. |
| Дубли/невалидные email | Реализовано | Исключаются из accepted recipients. |
| Global suppression | Реализовано | Публичная отписка, повторная отписка идемпотентна. |
| Client suppression по hard bounce | Реализовано | Sprint 8 добавляет client-level suppression. |
| Подтверждение базы | Реализовано | Есть декларация источника и согласий. |
| Редактор plain text письма | Реализовано | Preview показывает служебные блоки и demo unsubscribe placeholder. |
| Персонализация ФИО | Post-MVP | Не входит в текущий MVP для новых клиентов. |
| Fake-оплата | Реализовано | Реальные платёжные агенты не подключены. |
| Проверка/модерация | Частично реализовано | Deterministic risk engine и ручная admin moderation, persistence частично in-memory. |
| Отправка через домен сервиса | Частично реализовано | Для MVP используется fake provider; реальная доменная инфраструктура post-MVP/hardening. |
| Очередь отправки | Реализовано частично | In-memory/inline для dev, Hangfire+Postgres для send queue smoke. |
| Delivery webhooks | Реализовано | Fake webhooks accepted/delivered/bounce/complaint/rejected/unknown. |
| Ответы получателей | Реализовано | Fake inbound replies, reply forwarding и cleanup тела ответа. |
| Админ-контроль рисков | Реализовано | Клиенты, блокировки, лимиты, настройки MVP, audit, жалобы/ошибки/ответы. |
| Audit log | Частично реализовано | Есть dev/in-memory и EF-срезы для части событий; production retention требует hardening. |

## Технические критерии Sprint 11

| Критерий | Статус | Комментарий |
| --- | --- | --- |
| Dev/in-memory full e2e | Реализовано как целевой checklist | Проверяется по `docs/demo_checklist.md`. |
| Postgres+Hangfire smoke | Частично реализовано | Проверяет устойчивые storage/queue части. |
| Документация локального запуска | Реализовано | `docs/local_run.md`. |
| Правила для LLM-агентов | Реализовано | `docs/llm_agent_guide.md`. |
| README как короткий индекс | Реализуется Sprint 11 | README должен ссылаться на docs, а не заменять их. |
| Dev-only endpoints закрыты вне Development | Частично реализовано | Основные dev endpoints мапятся только в Development; требуется smoke. |
| Нет реальных внешних платежей/отправок | Реализовано для smoke | Fake provider/fake payment используются по умолчанию для demo. |
| dotnet format/build/test | Требует локального прогона | В среде агента checkout GitHub недоступен; владелец должен прогнать команды локально. |

## Известные ограничения MVP

- Реальный email provider не подключён к smoke flow.
- Реальный платёжный агент не подключён.
- Payment/risk/moderation/settings persistence остаётся частично in-memory/dev-срезом.
- Token payload для unsubscribe/reply подписан, но не является полноценной production privacy-архитектурой без дополнительного hardening.
- Delivery analytics ограничена MVP-сводками.
- Нет полноценного design system.
- Нет мультидоменной отправки от клиентов.
- Нет персонализации по ФИО для новых клиентов.
- Нет production-grade retention policy для всех historical provider payload/body.

## Freeze-правило Sprint 11

Sprint 11 стабилизирует MVP-testing и demo-flow. Новые production-требования не блокируют MVP, если они не были частью согласованного MVP. Их нужно явно оформлять как:

- production hardening;
- post-MVP;
- отдельный ADR;
- отдельный sprint backlog item.

## Решение о приёмке MVP

MVP можно передавать на первичное тестирование, если:

1. `docs/demo_checklist.md` проходит в dev/in-memory режиме.
2. Postgres+Hangfire smoke проходит по устойчивым storage/queue частям.
3. `dotnet build` и `dotnet test` проходят локально.
4. Известные ограничения из этого документа приняты как ограничения MVP, а не скрытые production-дефекты.

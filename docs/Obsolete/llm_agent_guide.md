# LLM agent guide для Письмолёта

Этот документ нужен, чтобы следующий LLM-агент не ломал архитектуру, безопасность и MVP-границы проекта.

## Контекст проекта

Письмолёт — MVP web-сервиса простых email-рассылок для малого бизнеса, НКО и нетехнических пользователей. Основной сценарий: импорт адресов, подтверждение базы, подготовка письма, fake-оплата, проверка, fake-отправка, отписка, webhook-статистика, ответы и admin-контроль.

## Архитектурные границы

- `src/Pismolet.Domain` — доменные модели, enum, чистые правила. Не зависит от Web, EF, Hangfire, HTTP и внешних сервисов.
- `src/Pismolet.Application` — сценарии, application services, интерфейсы persistence/provider, бизнес-проверки.
- `src/Pismolet.Infrastructure` — EF/PostgreSQL, in-memory fallback, fake provider, Hangfire, SMTP adapter, seed.
- `src/Pismolet.Web` — endpoints, auth policies, HTML rendering, HTTP формы.
- `tests/Pismolet.Web.Tests` — unit/integration тесты текущего среза.
- `docs` — актуальная инструкция запуска, demo checklist, acceptance и правила для агентов.

## Коммиты

Все коммиты должны быть на русском языке и начинаться с префикса чата-автора:

- `[Технарь] ...` — технические изменения;
- `[Юрист] ...` — юридические изменения;
- `[SEO] ...` — SEO/маркетинг;
- `[Общий] ...` — продуктовые/общие изменения.

Для этого чата используйте `[Технарь]`.

## Опасные зоны, требующие review

Нельзя менять без явного review:

- auth/admin policy и allowlist;
- payment flow и idempotency fake payment;
- формат unsubscribe token;
- формат inbound reply token;
- webhook secret validation;
- suppression rules: global suppression, client suppression, complaint side effects;
- moderation/risk rules;
- provider adapter contract;
- background job idempotency;
- хранение персональных данных, raw payload, reply body и retention;
- реальные email/payment provider integrations;
- EF migrations, которые меняют уже существующие таблицы;
- dev-only endpoint guards.

## Fake provider contract

В Sprint 11 smoke используются только fake provider и fake payment.

Тестовые адреса:

- `ok@example.test` — успешная fake-отправка;
- `please-fail@example.test` — provider error;
- `temp@example.test` — временная fake-ошибка;
- `hard-bounce@example.test` — кандидат для fake hard bounce;
- `complaint@example.test` — кандидат для fake complaint;
- `unsubscribed@example.test` — demo global suppression.

Preview письма не должен создавать реальные unsubscribe/reply tokens. Рабочие tokens создаются только при фактической fake-отправке конкретному адресату.

## Правила тестирования

Добавляйте тесты на уровне, где живёт правило:

- unit tests для доменных state machines и чистых сервисов;
- application tests для idempotency и бизнес-проверок;
- integration tests для HTTP endpoints;
- fake provider tests для delivery/reply контрактов;
- negative tests для import/payment/send/webhook/unsubscribe/reply/admin блокировок.

Не создавайте один неподдерживаемый тест на сотни шагов, если его можно заменить устойчивым набором тестов с общими helpers.

## State machine и idempotency

Любой новый шаг должен учитывать:

- refresh страницы не создаёт дублей;
- повторный callback/webhook/job идемпотентен;
- blocked client/mailing останавливает backend-flow;
- отправка без оплаты и одобрения запрещена backend-уровнем;
- технические enum/status не показываются пользователю без русского текста.

## Безопасность и данные

- Обычный пользователь не должен видеть provider payload, raw webhook payload, reply body после TTL, unsubscribe token, reply token и secrets.
- Admin pages должны иметь backend guard, а не только скрытую кнопку в UI.
- Dev endpoints доступны только в `Development` или через явный config flag, если это уже предусмотрено кодом.
- Demo данные должны использовать `.test` или `.local`, не реальные адреса.

## Как вносить изменения

1. Сначала прочитайте task-файл нужного sprint в `source/tasks`.
2. Проверьте существующие сервисы и endpoints, не создавайте параллельный модуль.
3. Вносите изменения в тот слой, где находится правило.
4. Добавьте или обновите тесты.
5. Обновите README/docs, если меняется flow.
6. Сообщите изменённые файлы, краткое описание и commit SHA.

## Что не делать в Sprint 11

- Не подключать реальные email/payment provider.
- Не расширять MVP production-обязательствами.
- Не переписывать архитектуру без ADR.
- Не хранить raw данные дольше, чем требуется MVP-debug.
- Не превращать Sprint 11 в redesign.

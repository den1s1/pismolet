# Sprint 11 — MVP-полировка и end-to-end сценарий

Ветка: `Development`  
Основание: `docs/sprints.md`, раздел «Спринт 11 — MVP-полировка и end-to-end сценарий»  
Дата актуализации: 2026-06-21  
Статус: backlog для реализации

## 1. Текущий срез кода

По состоянию ветки `Development` перед Sprint 11:

- основной вертикальный MVP-flow уже собран по спринтам 0–10;
- в `README.md` зафиксированы рабочие маршруты регистрации, личного кабинета, создания рассылки, импорта, декларации, письма, fake-оплаты, проверки, fake-отправки, публичной отписки, fake webhooks, inbound replies и админки;
- Sprint 6 дал fake-отправку через `FakeEmailProviderAdapter`, `SendEvent`, provider message id, дневные лимиты и Hangfire/Postgres production-срез очереди;
- Sprint 7 дал публичную отписку, `GlobalSuppression` и исключение suppressed email из будущих импортов/отправок;
- Sprint 8 дал fake provider webhooks: `accepted`, `delivered`, `soft_bounce`, `hard_bounce`, `complaint`, `rejected`, `unknown`;
- Sprint 9 дал inbound replies, `ReplyEvent` и fake-пересылку ответа клиенту;
- Sprint 10 дал admin-контроль: клиенты, блокировки, лимиты, настройки MVP, audit log, импорты, complaints, delivery errors, replies;
- production-срез storage уже частично есть для `global_suppressions`, `send_events`, `provider_webhook_events`, `client_suppressions`;
- часть storage всё ещё остаётся in-memory/dev-срезом: payment/risk/moderation и отдельные настройки цены/админских параметров;
- в Development уже есть startup seed/migration hook вида `SeedPismoletDevData()`; Sprint 11 должен расширять или заменить его явно, а не создавать второй несогласованный seed-механизм;
- документация локального запуска и smoke-проверок уже есть частично, но её нужно привести к единому demo/e2e checklist.

Sprint 11 — это не новый функциональный модуль. Главная задача — стабилизировать MVP как демонстрируемый end-to-end продукт: убрать тупики UX, проверить state machine, сделать воспроизводимый demo seed, закрыть основные ошибки, оформить локальный запуск и инструкции для LLM-агентов.

## 2. Цель Sprint 11

Довести продукт до первичного тестирования и демонстрации: один человек должен локально поднять проект, пройти полный сценарий и получить понятные статусы без ручного вмешательства в БД.

Целевой полный сценарий:

```text
регистрация → подтверждение email → профиль клиента → создание рассылки → импорт CSV/XLSX → декларация базы → редактор письма → preview → расчёт стоимости → fake-оплата → risk-проверка → автоодобрение или ручная модерация → запуск отправки → fake delivery/bounce/complaint → публичная отписка → inbound reply → статистика → admin audit
```

## 3. Обязательные рамки Sprint 11

- Не добавлять новый большой продуктовый модуль, пока не стабилизирован полный MVP-flow.
- Не переписывать архитектуру слоёв `Domain` / `Application` / `Infrastructure` / `Web` без отдельного ADR.
- Не менять принятый стек .NET / ASP.NET Core / Razor Pages / EF Core / PostgreSQL / Hangfire.
- Не подключать реального платёжного агента и реального email provider в рамках Sprint 11.
- Smoke/demo flow должен явно использовать fake provider и fake payment. Любые реальные provider/payment settings должны быть отсутствующими, выключенными или требовать отдельного explicit enable flag.
- Не заменять fake provider contract без синхронизации `README.md`, task-файлов и тестов.
- Не хранить raw email body, reply body, provider payload и токены дольше/шире, чем нужно для MVP-debug.
- Не показывать пользователю технические сущности `Campaign`, `SendEvent`, `PaymentAttempt`, `ProviderWebhookEvent` без пользовательского перевода.
- Все ошибки должны иметь понятный русский текст и безопасный fallback.
- P0 full e2e должен проходить в dev/in-memory режиме.
- Postgres+Hangfire smoke должен проверять уже устойчивые storage-части: `global_suppressions`, `send_events`, `provider_webhook_events`, `client_suppressions`, очередь отправки/обработки. Полная production persistence для payment/risk/moderation/settings не является P0 Sprint 11, если не была реализована раньше.
- Все dev-only endpoints должны быть явно недоступны вне Development-окружения.
- Финальный smoke checklist должен быть выполним без знания внутренней архитектуры.
- Sprint 11 фиксирует MVP acceptance. Новые production-требования нельзя молча добавлять в MVP; их нужно отмечать как post-MVP или production hardening.

## 4. Приоритеты Sprint 11

### P0 — обязательно для MVP-testing

Без P0 Sprint 11 нельзя считать завершённым:

- единый e2e smoke checklist от регистрации до статистики;
- стабильный happy path в dev/in-memory без ручного редактирования БД;
- отдельный Postgres+Hangfire smoke по устойчивым storage/queue частям;
- обработка ключевых ошибок импорта, оплаты, проверки, отправки, webhook, unsubscribe и inbound reply;
- seed/demo данные для быстрой проверки на базе существующего `SeedPismoletDevData()` или явной миграции к config-driven seed mode;
- понятные статусы и CTA на каждом шаге рассылки;
- базовые regression tests для state machine рассылки и импорта;
- интеграционный happy path как один тест или набор устойчивых тестов с общими helpers;
- обязательные документы: `docs/demo_checklist.md`, `docs/local_run.md`, `docs/llm_agent_guide.md`, `docs/mvp_acceptance.md`;
- README как короткий индекс со ссылками на документы;
- финальная сверка MVP acceptance criteria без расширения до production hardening.

### P1 — желательно после P0

- улучшение мобильной ширины;
- приведение UI к HTML-макетам из `ui`;
- расширенные пустые состояния;
- дополнительные demo CSV/XLSX;
- debug-страница состояния системы;
- улучшенная навигация между шагами;
- дополнительные интеграционные сценарии с bounce/complaint/reply.

### P2 — не блокирует Sprint 11

- production persistence для всех оставшихся in-memory участков;
- полноценная аналитика;
- реальный email provider;
- реальный платёжный агент;
- advanced deliverability dashboard;
- полноценный design system.

## 5. Задачи реализации

### T11-01. Составить и закрепить единый MVP e2e checklist

**Цель:** получить один основной сценарий проверки MVP.

Задачи:

- Создать или обновить `docs/demo_checklist.md`.
- Описать полный happy path:
  - регистрация;
  - подтверждение email через dev/fake mailer;
  - создание профиля клиента;
  - создание рассылки;
  - импорт CSV;
  - декларация базы;
  - письмо и preview;
  - расчёт стоимости;
  - fake-оплата;
  - risk-проверка;
  - ручная модерация при необходимости;
  - запуск отправки;
  - fake webhook delivery;
  - публичная отписка;
  - inbound reply;
  - просмотр статистики;
  - проверка admin audit.
- Для каждого шага указать URL, ожидаемый статус и ожидаемый пользовательский текст.
- Добавить отдельный блок «что делать, если шаг не прошёл».
- Разделить checklist на два режима:
  - dev/in-memory full e2e;
  - Postgres+Hangfire smoke по устойчивым storage/queue частям.

Acceptance criteria:

- Чеклист можно пройти с чистой БД.
- Чеклист не требует ручной правки данных в БД.
- Для каждого шага указан ожидаемый результат.
- README ссылается на checklist.

### T11-02. Проверить и стабилизировать state machine рассылки

**Цель:** исключить невозможные переходы статусов и тупики UI.

Задачи:

- Описать допустимые статусы рассылки в одном месте.
- Проверить переходы:
  - `draft` → import/declaration/message;
  - `priced` / `payment_pending` / `paid`;
  - `pending_checks` / `approved` / `review_required` / `rejected`;
  - `sending` / `sent` / `failed` / `paused` / `blocked`.
- Запретить переход к отправке без оплаты.
- Запретить отправку без одобрения.
- Запретить повторный старт отправки, если рассылка уже отправляется или завершена.
- Запретить действия с заблокированным клиентом/рассылкой.
- Проверить refresh/retry сценарии.
- Добавить понятные CTA для каждого состояния.

Acceptance criteria:

- Нет UI-страницы, где пользователь остаётся без следующего действия или понятного статуса.
- Невозможные переходы отклоняются на backend уровне.
- Повторный refresh не создаёт дублей оплаты, проверки, отправки или webhook events.
- Unit-тесты покрывают основные переходы state machine.

### T11-03. Проверить и стабилизировать state machine импорта

**Цель:** сделать импорт предсказуемым для CSV/XLSX и ошибок файла.

Задачи:

- Проверить сценарии:
  - корректный CSV с `email`;
  - корректный XLSX, если поддержка уже включена;
  - пустой файл;
  - файл без email-колонки;
  - файл с невалидными email;
  - файл только с дублями;
  - файл, где все адреса suppressed/blocked;
  - повторная загрузка файла для той же рассылки.
- Убедиться, что пользователь видит агрегированную статистику импорта.
- Не показывать лишние персональные данные подавленных/отписанных адресов.
- Сформировать demo-файлы в `docs/examples` или `source/tasks/examples`, если такой папки ещё нет.
- Проверить, что импорт не ломает следующий шаг декларации.

Acceptance criteria:

- Ошибки импорта отображаются понятным русским текстом.
- При отсутствии валидных адресов нельзя продолжить к оплате/отправке.
- Suppressed/blocked email не раскрываются сверх агрегированных счётчиков.
- Unit/integration-тесты покрывают критичные сценарии импорта.

### T11-04. Привести UX шагов рассылки к единой логике

**Цель:** пользователь должен понимать, где он находится и что делать дальше.

Задачи:

- Добавить или унифицировать stepper/прогресс по шагам:
  - адресаты;
  - декларация;
  - письмо;
  - оплата;
  - проверка;
  - отправка;
  - статистика.
- На карточке рассылки показывать текущий статус человекочитаемо.
- На каждом шаге показывать следующий допустимый CTA.
- В недоступных шагах показывать причину, а не generic error.
- Проверить пустые состояния.
- Проверить тексты ошибок и предупреждений.
- Проверить, что технические названия enum/status не просачиваются в UI.

Acceptance criteria:

- Пользователь может пройти весь flow, переходя по кнопкам, а не вручную меняя URL.
- Каждый заблокированный шаг объясняет, что нужно сделать раньше.
- Все основные статусы отображаются на русском.

### T11-05. Доработать обработку ошибок fake-оплаты

**Цель:** fake payment остаётся dev-механикой, но ведёт себя предсказуемо.

Задачи:

- Проверить повторную fake-оплату.
- Проверить повторный callback/webhook оплаты.
- Проверить ошибочный сценарий fake-оплаты.
- Не создавать повторное списание/attempt на повторный callback.
- Показать пользователю понятный статус:
  - ожидает оплаты;
  - оплачено;
  - ошибка оплаты;
  - повторная оплата не требуется.
- Убедиться, что изменение цены после расчёта не меняет уже созданную оплату.

Acceptance criteria:

- Fake payment idempotent.
- Ошибка оплаты не переводит рассылку в отправку.
- Пользователь может повторить оплату только в допустимом состоянии.

### T11-06. Доработать обработку ошибок fake provider / очереди

**Цель:** отправка не должна терять состояние при ошибках provider/job.

Задачи:

- Проверить адреса `ok@example.test`, `please-fail@example.test`, `temp@example.test`.
- Проверить batch, где часть адресов успешна, часть с ошибками.
- Проверить retry job без дублей `SendEvent`.
- Проверить skipped events для suppressed/unsubscribed recipients.
- Проверить дневной лимит при повторном запуске.
- Проверить поведение при ошибке fake provider adapter.
- Проверить user-facing summary: отправлено / ошибка / пропущено / отписано.

Acceptance criteria:

- Ошибка одного адреса не ломает всю рассылку без явной причины.
- Повторный job не дублирует отправку уже обработанных recipient.
- Статистика остаётся согласованной с `SendEvent`.

### T11-07. Доработать обработку webhook и inbound reply ошибок

**Цель:** dev webhooks и inbound replies должны быть безопасными и идемпотентными.

Задачи:

- Проверить fake webhook secret.
- Проверить неизвестный `providerMessageId`.
- Проверить повторный `providerEventId`.
- Проверить неизвестный event type.
- Проверить hard bounce и complaint suppression side effects.
- Проверить inbound reply без matching token/header.
- Проверить cleanup временного тела reply.
- Проверить, что raw payload/body не показывается обычному пользователю.

Acceptance criteria:

- Повторные webhooks не удваивают статистику.
- Неизвестные webhooks безопасно логируются.
- Complaint добавляет глобальное подавление.
- Hard bounce добавляет client-level suppression.
- Reply без идентификации не ломает сервис.

### T11-08. Подготовить seed/demo сценарии

**Цель:** упростить проверку MVP без ручного создания всех данных.

Задачи:

- Использовать и расширить существующий Development seed hook `SeedPismoletDevData()` либо явно заменить его конфигурируемым seed mode.
- Не создавать параллельный seed endpoint/service, который расходится с существующим startup seed.
- Seed должен быть явно отключён в production-like окружении.
- Добавить demo admin user или инструкцию, как сделать пользователя админом через `Admin:AllowedEmails` / `PISMOLET_ADMIN_EMAILS`.
- Добавить demo client profile.
- Добавить demo mailing draft или набор demo CSV/XLSX.
- Добавить demo recipients:
  - успешный адрес;
  - provider error;
  - temp error;
  - hard bounce candidate;
  - complaint candidate;
  - already suppressed email.
- Зафиксировать seed contract в `docs/local_run.md` и `docs/demo_checklist.md`.

Acceptance criteria:

- Разработчик может быстро получить demo-сценарий без ручного SQL.
- Seed не включается случайно вне Development.
- Demo данные не используют реальные email-адреса.
- В проекте один согласованный seed-механизм или явно задокументированная миграция от старого к новому.

### T11-09. Документировать локальный запуск

**Цель:** новый разработчик или LLM-агент должен поднять проект по инструкции.

Задачи:

- Создать или обновить обязательный документ `docs/local_run.md`.
- README оставить коротким индексом со ссылкой на `docs/local_run.md`, `docs/demo_checklist.md`, `docs/llm_agent_guide.md`, `docs/mvp_acceptance.md`.
- Описать запуск in-memory/dev profile.
- Описать Postgres+Hangfire smoke profile и явно указать, какие части проверяются как устойчивые storage/queue части.
- Описать, что payment/risk/moderation/settings могут оставаться in-memory/dev-срезом, если production persistence для них не была реализована ранее.
- Описать миграции и ожидаемые таблицы.
- Описать обязательные настройки:
  - `Persistence:Provider`;
  - `ConnectionStrings:PismoletDb`;
  - `Sending:Queue`;
  - `Hangfire:*`;
  - `Unsubscribe:Secret`;
  - `Webhooks:FakeProviderSecret`;
  - `Admin:AllowedEmails` / `PISMOLET_ADMIN_EMAILS`.
- Описать команды:
  - `dotnet restore`;
  - `dotnet format --verify-no-changes`;
  - `dotnet build`;
  - `dotnet test`;
  - `dotnet run`.
- Добавить troubleshooting:
  - не применились миграции;
  - не работает Hangfire;
  - нет доступа в admin;
  - webhook отклоняется;
  - unsubscribe token невалиден;
  - fake provider/payment случайно заменён реальной конфигурацией.

Acceptance criteria:

- `docs/local_run.md` самодостаточен для локального запуска.
- Инструкция различает dev/in-memory full e2e и Postgres+Hangfire smoke.
- README содержит ссылки на обязательные документы, а не пытается вместить всю детализацию.

### T11-10. Документировать правила для LLM-агентов

**Цель:** снизить риск, что следующий агент сломает архитектуру или безопасность.

Задачи:

- Создать или обновить обязательный документ `docs/llm_agent_guide.md`.
- Зафиксировать архитектурные границы:
  - `Domain` не зависит от infrastructure/web;
  - `Application` содержит сценарии и интерфейсы;
  - `Infrastructure` содержит EF/Hangfire/fake provider;
  - `Web` содержит endpoints/rendering.
- Описать, что нельзя менять без review:
  - auth/admin policy;
  - payment flow;
  - unsubscribe token format;
  - webhook secret validation;
  - suppression rules;
  - moderation/risk rules;
  - provider adapter contract;
  - background job idempotency;
  - персональные данные и retention.
- Описать как писать тесты:
  - unit для state machines и сервисов;
  - integration для HTTP flow;
  - fake provider tests;
  - idempotency tests.
- Описать commit message convention проекта: `[Технарь] ...`.

Acceptance criteria:

- Новый LLM-агент понимает, куда вносить изменения.
- В документе есть список опасных зон, требующих review.
- В документе есть правила тестирования и fake provider contract.

### T11-11. Финальная сверка acceptance criteria MVP

**Цель:** понять, что MVP действительно готов к первичному тестированию.

Задачи:

- Создать или обновить обязательный документ `docs/mvp_acceptance.md`.
- Сопоставить acceptance criteria с `docs/specification.md`, `docs/platform_tz.md`, `docs/sprints.md`.
- Отметить для каждого критерия:
  - реализовано;
  - частично реализовано;
  - не входит в MVP;
  - требует отдельного production hardening;
  - post-MVP.
- Проверить продуктовые обязательства:
  - простая рассылка без BCC;
  - импорт адресов;
  - подтверждение базы;
  - fake-оплата;
  - проверка/модерация;
  - отправка через домен сервиса/fake provider;
  - глобальная отписка;
  - ответы получателей;
  - админ-контроль рисков.
- Отдельно перечислить известные ограничения MVP.
- Зафиксировать freeze-правило: Sprint 11 не расширяет MVP новыми production-обязательствами. Новые требования записываются как post-MVP / production hardening, если они не нужны для локального demo.

Acceptance criteria:

- Есть один документ, по которому можно принять или не принять MVP.
- Все известные ограничения явно названы.
- Нет скрытых production-обещаний, которых код не выполняет.
- Новые production-требования не блокируют MVP-testing, если не были частью согласованного MVP.

### T11-12. Покрыть happy path интеграционным тестом или набором тестов

**Цель:** защитить основной flow от регрессий.

Задачи:

- Добавить integration test или набор integration tests полного happy path.
- Разрешён набор устойчивых тестов с общим helper/factory; не требуется один гигантский тест на 200 шагов, если он становится хрупким.
- Тесты должны проходить ключевые HTTP endpoints в порядке пользовательского сценария.
- Где невозможно пройти реальную авторизацию через UI, использовать существующие test helpers, но не обходить бизнес-правила.
- Проверить, что после отправки есть `SendEvent` и корректная статистика.
- Проверить, что fake webhook обновляет delivery-сводку.
- Проверить, что unsubscribe исключает адрес в следующем сценарии.
- Проверить, что inbound reply создаёт счётчик/событие.

Acceptance criteria:

- Один тест или набор тестов доказывает основной e2e flow.
- Тесты не зависят от порядка запуска других тестов.
- Тесты не используют реальные внешние сервисы.
- Тесты читаемы и не превращаются в неподдерживаемый монолит.

### T11-13. Добавить regression tests для критичных негативных сценариев

**Цель:** закрепить поведение ошибок MVP.

Задачи:

- Добавить тесты:
  - пустой CSV;
  - CSV без email;
  - нет валидных адресов;
  - рассылка без декларации;
  - рассылка без оплаты;
  - отправка без одобрения;
  - повторная fake-оплата;
  - повторный send job;
  - повторный webhook;
  - заблокированный клиент;
  - complaint suppression;
  - unsubscribe idempotency.
- Разнести unit/integration tests по существующей структуре.

Acceptance criteria:

- Критичные ошибки проверяются автоматическими тестами.
- Тесты читаемы и описывают бизнес-правило, а не только техническую реализацию.

### T11-14. Проверить UI на мобильной ширине и HTML-макеты

**Цель:** убрать грубые визуальные проблемы перед демонстрацией.

Задачи:

- Проверить основные страницы на мобильной ширине:
  - главная;
  - регистрация/логин;
  - dashboard;
  - создание рассылки;
  - импорт;
  - декларация;
  - письмо;
  - оплата;
  - проверки;
  - отправка/статистика;
  - unsubscribe;
  - admin dashboard.
- Сравнить ключевые страницы с HTML-макетами из `ui`, если они есть в репозитории.
- Исправить грубые разрывы layout, слишком широкие таблицы, нечитаемые кнопки.
- Не превращать Sprint 11 в полный redesign.

Acceptance criteria:

- Основной flow можно пройти на узкой ширине экрана.
- Таблицы/статусы не ломают страницу.
- UI не противоречит базовым макетам.

### T11-15. Финальный hardening dev-only и security flags

**Цель:** исключить случайный доступ к dev-инструментам и реальные внешние действия в MVP smoke.

Задачи:

- Проверить `/dev/fake-mailer`.
- Проверить `/dev/webhooks/fake`.
- Проверить seed/demo endpoints или startup seed mode.
- Проверить dev admin allowlist warnings.
- Проверить, что fake secrets не имеют production defaults без warning/fail-fast.
- Проверить, что raw payload/body/token не показываются обычному пользователю.
- Проверить, что admin pages имеют backend auth guard.
- Проверить, что fake provider/fake payment используются в smoke/demo flow.
- Проверить, что реальные email/payment provider settings либо отсутствуют, либо выключены, либо требуют explicit enable flag вне Sprint 11.

Acceptance criteria:

- Dev-only endpoints закрыты вне Development.
- Слабые секреты дают warning или fail-fast в production-like конфигурации.
- Обычный пользователь не видит debug/raw данные.
- Финальный smoke не создаёт реальных внешних отправок и платежей.

## 6. Unit-тесты Sprint 11

Обязательный минимум:

- state machine рассылки;
- state machine импорта;
- pricing idempotency;
- fake payment idempotency;
- suppression rules;
- moderation/risk transitions;
- sending idempotency;
- webhook idempotency;
- unsubscribe idempotency;
- reply cleanup;
- blocked client / blocked mailing policy;
- admin settings application rules;
- seed/demo guard rules;
- MVP acceptance freeze rules, если они вынесены в кодовые checks.

## 7. Интеграционные тесты Sprint 11

Обязательный минимум:

- полный happy path как один тест или набор устойчивых тестов;
- сценарий с ручной модерацией;
- сценарий с отпиской;
- сценарий с hard bounce;
- сценарий с complaint;
- сценарий с inbound reply;
- сценарий с заблокированным клиентом;
- повторный fake payment callback;
- повторный send job;
- повторный webhook event;
- dev/in-memory full e2e;
- Postgres+Hangfire smoke, если test setup позволяет;
- запрет dev-only endpoints вне Development, если test setup позволяет.

## 8. Ручные тесты Sprint 11

Минимальный ручной checklist:

1. Поднять приложение в dev/in-memory режиме.
2. Зарегистрировать пользователя.
3. Подтвердить email через `/dev/fake-mailer`.
4. Создать профиль клиента.
5. Создать рассылку.
6. Загрузить маленький CSV с 3–5 адресами.
7. Пройти декларацию базы.
8. Заполнить письмо.
9. Проверить preview служебных блоков.
10. Пройти расчёт стоимости.
11. Пройти fake-оплату.
12. Запустить risk-проверку.
13. При необходимости одобрить через `/admin/moderation`.
14. Запустить отправку.
15. Проверить статистику отправки.
16. Отправить fake delivery webhook.
17. Отправить fake hard bounce.
18. Отправить fake complaint.
19. Открыть unsubscribe link из fake mailer.
20. Отправить fake inbound reply.
21. Проверить admin audit.
22. Повторить smoke test с Postgres+Hangfire profile по устойчивым storage/queue частям.
23. Проверить мобильную ширину.
24. Проверить CSV с ошибками.
25. Проверить refresh/retry на оплате, отправке и webhook.
26. Проверить, что fake provider/payment не заменены реальными интеграциями в smoke flow.

## 9. Definition of Done Sprint 11

Sprint 11 считается завершённым, если:

- полный `docs/demo_checklist.md` проходит локально;
- dev/in-memory full e2e проходит без ручной правки БД;
- Postgres+Hangfire smoke проходит по устойчивым storage/queue частям;
- основной happy path покрыт интеграционным тестом или набором устойчивых тестов;
- критичные негативные сценарии покрыты unit/integration tests;
- пользовательские ошибки отображаются понятным русским текстом;
- нет тупиков UI в основном flow;
- fake provider, fake payment, unsubscribe, webhook, inbound reply и admin audit работают в связке;
- есть обязательные документы: `docs/demo_checklist.md`, `docs/local_run.md`, `docs/llm_agent_guide.md`, `docs/mvp_acceptance.md`;
- README содержит короткий индекс и ссылки на обязательные документы;
- MVP acceptance фиксирует известные ограничения и не расширяет Sprint 11 до production hardening;
- `dotnet format --verify-no-changes`, `dotnet build` и `dotnet test` проходят;
- dev-only endpoints не доступны вне Development;
- все изменения не создают реальных внешних отправок и платежей.

## 10. Документы, требующие синхронизации

В рамках Sprint 11 нужно синхронизировать:

- `README.md` — короткий индекс, ссылки на demo checklist, local run, LLM guide, MVP acceptance, актуальные маршруты;
- `docs/demo_checklist.md` — полный ручной e2e сценарий;
- `docs/local_run.md` — подробный запуск dev/in-memory и Postgres+Hangfire smoke;
- `docs/llm_agent_guide.md` — правила для LLM-агентов;
- `docs/mvp_acceptance.md` — критерии приёмки MVP, известные ограничения, post-MVP / production hardening;
- `docs/platform_tz.md` — только если в процессе hardening меняются архитектурные решения;
- `docs/specification.md` — менять не требуется, если не меняются продуктовые решения.

## 11. Что не входит в Sprint 11

- Реальный email provider.
- Реальный платёжный агент.
- Полная production deliverability-инфраструктура.
- Production persistence для всех оставшихся in-memory/dev-срезов, если это не было реализовано раньше.
- Массовая аналитика и BI.
- Полный redesign UI.
- Мультидоменная отправка от клиентов.
- Персонализация по ФИО для новых клиентов сверх уже согласованной модели.
- Production-grade хранение всех исторических provider payload без отдельной retention policy.
- Расширение MVP acceptance новыми production-обязательствами без отдельного решения.

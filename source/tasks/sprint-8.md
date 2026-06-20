# Sprint 8 — webhooks: delivery, bounce, complaint

Ветка: `Development`  
Основание: `docs/sprints.md`, раздел «Спринт 8 — webhooks: delivery, bounce, complaint»  
Дата актуализации: 2026-06-20  
Статус: backlog для реализации

## 1. Текущий срез кода

По состоянию ветки `Development` перед Sprint 8:

- Sprint 6 должен дать отправку через очередь и fake email provider;
- Sprint 6 должен ввести `IEmailProviderAdapter`, `SendEvent`, `providerMessageId`, статусы отправки, progress и summary отправки;
- Sprint 7 должен дать `GlobalSuppression`, защищённые unsubscribe tokens, публичный unsubscribe flow и исключение отписанных адресов при импорте/отправке;
- Sprint 8 продолжает flow после факта отправки: он не отправляет письма заново, а принимает события провайдера и обновляет состояние доставки;
- обработка реальных SMTP/API-провайдеров всё ещё не входит в MVP: в Sprint 8 используется fake provider/fake webhook sender, но архитектура должна быть готова к реальному провайдеру;
- webhooks должны быть идемпотентными, потому что реальные email-провайдеры могут присылать одно и то же событие повторно;
- complaint должен использовать тот же механизм глобальной отписки, что и Sprint 7;
- hard bounce должен блокировать будущие отправки этому адресу в рамках конкретного клиента, а не обязательно глобально для всего сервиса.

Sprint 8 должен продолжать существующий MVP-flow, а не создавать отдельный модуль аналитики.

## 2. Цель Sprint 8

Система принимает события от email-провайдера и обновляет статистику рассылки.

Рабочий сценарий Sprint 8:

```text
создать рассылку → отправить через fake provider → получить providerMessageId → отправить fake webhook accepted/delivered/bounce/complaint → система идемпотентно обновляет SendEvent/статистику → пользователь видит доставлено/ошибка/жалоба → complaint добавляет GlobalSuppression → hard bounce блокирует будущие отправки этому адресу у клиента
```

## 3. Обязательные рамки Sprint 8

- Webhook endpoints в MVP работают с fake provider, но их контракт должен быть провайдер-независимым.
- Нельзя обновлять статистику только на уровне HTML/UI: состояние событий должно храниться устойчиво.
- Входные webhook-события должны проходить через `IEmailProviderAdapter` или отдельный provider parser, а не парситься прямо в endpoint.
- Повторный webhook не должен создавать дубли, повторно увеличивать счётчики или повторно добавлять suppression/blocklist.
- Неизвестное событие нужно безопасно логировать и не ломать flow.
- Hard bounce блокирует будущие отправки этому адресу в рамках клиента.
- Complaint добавляет email в `GlobalSuppression` глобально для всего сервиса.
- Soft bounce не должен сразу глобально или клиентски блокировать email, но должен отображаться как временная ошибка доставки.
- Webhook endpoints не должны раскрывать внутренние данные пользователю.
- Dev/fake webhook sender разрешён только для локальной проверки и не должен выглядеть как пользовательская функция клиента.
- Пользовательский язык интерфейса — русский.

## 4. Задачи реализации

### T8-01. Уточнить доменную модель событий доставки

**Цель:** отделить факт отправки письма от последующих событий провайдера.

Задачи:

- Проверить текущую модель `SendEvent` после Sprint 6.
- Выбрать один из вариантов:
  - расширить `SendEvent` историей provider events;
  - добавить отдельную сущность `DeliveryEvent` / `ProviderWebhookEvent`.
- Минимальные поля события провайдера:
  - `Id`;
  - `Provider`;
  - `ProviderEventId`;
  - `ProviderMessageId`;
  - `MailingId`;
  - `ClientId`;
  - `RecipientEmailNormalized` или stable recipient key;
  - `EventType` (`accepted`, `delivered`, `soft_bounce`, `hard_bounce`, `complaint`, `rejected`, `unknown`);
  - `OccurredAt` по данным провайдера;
  - `ReceivedAt` по времени сервиса;
  - `RawPayloadHash`;
  - `RawPayloadStored` / безопасный raw snapshot, если нужен для dev/admin диагностики;
  - `ReasonCode`;
  - `ReasonMessage`;
  - `ProcessingStatus` (`processed`, `ignored_duplicate`, `ignored_unknown`, `failed`).
- Добавить уникальность по `Provider + ProviderEventId`.
- Если у fake provider нет натурального `ProviderEventId`, генерировать стабильный id в fake webhook sender.
- Не использовать email в открытом виде в технических логах там, где достаточно normalized/hash.

Acceptance criteria:

- Одно provider-событие хранится один раз.
- Можно восстановить историю доставки конкретного письма.
- Можно построить агрегированную статистику рассылки без парсинга HTML.
- Модель не зависит от конкретного реального провайдера.

### T8-02. Расширить `IEmailProviderAdapter` парсингом webhook-событий

**Цель:** endpoint не должен знать формат конкретного провайдера.

Задачи:

- Добавить в provider layer контракт парсинга webhook payload, например:
  - `ParseWebhookAsync(...)`;
  - `ValidateWebhookSignatureAsync(...)`, если сигнатуры вводятся уже сейчас;
  - `GetProviderName()` / provider id.
- Ввести DTO/value object `EmailProviderWebhookEvent`.
- Поддержать события:
  - `accepted`;
  - `delivered`;
  - `soft_bounce`;
  - `hard_bounce`;
  - `complaint`;
  - `rejected`;
  - `unknown`.
- Для fake provider описать простой JSON-формат webhook payload.
- Не смешивать provider-specific названия с доменными enum: маппинг должен быть явным.
- Ошибки парсинга возвращать как controlled result, а не непойманное исключение endpoint.

Acceptance criteria:

- Endpoint вызывает adapter/parser и работает с нормализованным событием.
- Fake provider может сгенерировать все типы событий Sprint 8.
- Unknown/invalid payload не ломает приложение.
- Unit-тесты покрывают маппинг provider-specific event type → domain event type.

### T8-03. Добавить webhook endpoints

**Цель:** принять события доставки от fake provider через HTTP.

Задачи:

- Добавить `MapWebhookEndpoints()` или `MapProviderWebhookEndpoints()` в `Program.cs`.
- Минимальные endpoints:
  - `POST /webhooks/email/fake` для MVP;
  - опционально `POST /webhooks/email/{provider}` для provider-agnostic маршрута.
- Endpoint должен:
  - принять raw body;
  - определить provider;
  - проверить подпись/секрет, если включено;
  - передать payload в adapter/parser;
  - вызвать application service обработки события;
  - вернуть безопасный ответ без внутренних деталей.
- Для fake provider допустим dev-secret в конфигурации, например `Webhooks:FakeProviderSecret`.
- Добавить защиту от случайного публичного использования dev endpoint:
  - secret header;
  - или environment check;
  - или явное включение через конфигурацию.
- Не отдавать пользователю raw payload или stack trace при ошибке.

Acceptance criteria:

- `POST` валидного fake webhook приводит к сохранению события.
- `POST` без dev-secret/подписи отклоняется, если защита включена.
- Invalid payload возвращает безопасный 400/202 в зависимости от выбранной политики.
- Повторный webhook возвращает успех/нейтральный ответ, но не меняет статистику повторно.

### T8-04. Реализовать application service обработки provider events

**Цель:** централизованно применить webhook-событие к рассылке, получателю и статистике.

Задачи:

- Добавить сервис, например `EmailWebhookProcessingService`.
- Сервис должен:
  - найти `SendEvent` по `ProviderMessageId` и/или `MailingId + recipient key`;
  - проверить, что событие относится к известной отправке;
  - сохранить `ProviderWebhookEvent`;
  - обновить delivery-status конкретного recipient/send event;
  - пересчитать или инвалидировать summary;
  - обработать специальные последствия hard bounce и complaint;
  - безопасно залогировать unknown/unmatched событие.
- Обработать порядок событий:
  - `accepted` может прийти до/после локального сохранения отправки;
  - `delivered` может прийти после `accepted`;
  - `bounce` может прийти после `accepted`;
  - повторные и запоздалые события не должны ломать итоговый статус.
- Задать приоритет финальных статусов:
  - `complaint` сильнее `delivered`;
  - `hard_bounce` сильнее `accepted`;
  - `delivered` сильнее `accepted`;
  - `rejected`/`hard_bounce` сильнее `soft_bounce`, если событие финальное;
  - unknown не меняет итоговый delivery-status.
- Если событие невозможно сопоставить с отправкой, сохранить его как unmatched/ignored для аудита, но не создавать рассылку или получателя из webhook.

Acceptance criteria:

- Сервис идемпотентен по `Provider + ProviderEventId`.
- Сопоставленное событие обновляет delivery-status.
- Несопоставленное событие безопасно логируется.
- Финальный статус не ухудшается случайным старым/повторным accepted webhook.

### T8-05. Обновить статистику рассылки

**Цель:** пользователь видит статусы доставки, ошибок и жалоб.

Задачи:

- Расширить summary рассылки показателями:
  - отправлено provider/fake provider;
  - принято провайдером (`accepted`);
  - доставлено (`delivered`);
  - временная ошибка (`soft_bounce`);
  - постоянная ошибка (`hard_bounce`);
  - жалоба (`complaint`);
  - отклонено (`rejected`);
  - неизвестные/необработанные события для admin/dev.
- В пользовательском UI показывать простые русские подписи:
  - «Принято провайдером»;
  - «Доставлено»;
  - «Временная ошибка»;
  - «Постоянная ошибка»;
  - «Жалоба».
- Не показывать клиенту raw payload, provider event id и технические stack traces.
- Для admin/dev страницы можно показать provider event id и raw snapshot/hash.
- Статистика должна быть устойчивой после рестарта приложения.

Acceptance criteria:

- После fake delivered webhook счётчик «Доставлено» увеличивается один раз.
- После hard bounce счётчик «Постоянная ошибка» увеличивается один раз.
- После complaint счётчик «Жалоба» увеличивается один раз.
- Повторный webhook не меняет счётчики повторно.

### T8-06. Реализовать client-level блокировку hard bounce

**Цель:** не отправлять письма адресу, который получил постоянную ошибку у конкретного клиента.

Задачи:

- Добавить или использовать сущность клиентского suppression/blocklist, например `ClientSuppression`.
- Минимальные поля:
  - `Id`;
  - `ClientId`;
  - `EmailNormalized`;
  - `Reason` (`HardBounce`, опционально `ManualBlock`);
  - `SourceMailingId`;
  - `SourceProviderMessageId`;
  - `CreatedAt`;
  - `LastSeenAt`.
- Уникальность: `ClientId + EmailNormalized + Reason` или `ClientId + EmailNormalized`, если блокировка единая.
- Hard bounce должен добавлять/обновлять `ClientSuppression` идемпотентно.
- Импорт и отправка должны учитывать client-level suppression отдельно от `GlobalSuppression`.
- Клиенту можно показывать агрегированный счётчик «исключено из-за ошибки доставки», но не раскрывать лишние технические детали.

Acceptance criteria:

- После hard bounce повторная рассылка этого клиента не отправляет письмо на этот email.
- Другой клиент не блокируется из-за hard bounce первого клиента.
- Повторный hard bounce не создаёт дубль в client suppression.
- Импорт/отправка различают global unsubscribe и client hard bounce.

### T8-07. Обрабатывать complaint через `GlobalSuppression`

**Цель:** жалоба получателя должна глобально исключать email из будущих рассылок сервиса.

Задачи:

- При `complaint` добавлять email в `GlobalSuppression` через сервис Sprint 7.
- Source для suppression: `Complaint`.
- Сохранять ссылку на `MailingId`/`ProviderMessageId` только во внутреннем/audit-контексте.
- Повторный complaint должен быть идемпотентным.
- Если email уже был отписан через ссылку, complaint не должен создавать дубль.
- Перед отправкой и при импорте уже должен срабатывать существующий механизм исключения `GlobalSuppression`.

Acceptance criteria:

- Complaint создаёт или подтверждает `GlobalSuppression`.
- Повторный complaint не создаёт дубль.
- Следующая рассылка любого клиента исключает этот email.
- Пользователь не видит лишних деталей жалобы.

### T8-08. Добавить audit log и безопасное логирование webhook flow

**Цель:** сохранить след принятия и применения provider-событий без раскрытия лишних персональных данных.

Задачи:

- Логировать:
  - факт получения webhook;
  - provider;
  - event type;
  - provider event id;
  - matched/unmatched результат;
  - последствия (`delivery updated`, `client suppression added`, `global suppression added`, `duplicate ignored`);
  - ошибки парсинга/валидации.
- Не писать сырой email в обычные application logs, если достаточно hash/masked value.
- Raw payload хранить только если это явно нужно для dev/admin диагностики и безопасно ограничено.
- Добавить correlation id для webhook processing.
- Для duplicate webhook логировать duplicate ignored, но не считать это ошибкой.

Acceptance criteria:

- По audit log можно понять, что произошло с webhook.
- Логи не раскрывают лишние персональные данные.
- Duplicate/unknown события логируются предсказуемо.

### T8-09. Добавить fake webhook sender / dev-инструмент

**Цель:** дать разработчику и тестировщику возможность вручную проверить delivery/bounce/complaint без реального email-провайдера.

Задачи:

- Добавить dev/admin страницу или endpoint для генерации fake webhook events.
- Источник данных:
  - выбрать рассылку;
  - выбрать отправленное fake provider сообщение / `providerMessageId`;
  - выбрать event type;
  - отправить webhook в тот же processing pipeline, что и внешний endpoint.
- Поддержать события:
  - accepted;
  - delivered;
  - soft_bounce;
  - hard_bounce;
  - complaint;
  - rejected;
  - unknown.
- Fake sender должен генерировать стабильный `ProviderEventId` для проверки идемпотентности или позволять повторить тот же event id.
- Dev-инструмент должен быть недоступен как обычная клиентская функция.

Acceptance criteria:

- Через dev-инструмент можно отправить fake delivered webhook.
- Можно повторить тот же webhook и увидеть, что статистика не удвоилась.
- Можно отправить hard bounce и проверить client suppression.
- Можно отправить complaint и проверить global suppression.

### T8-10. Обновить UI статуса рассылки после provider events

**Цель:** пользователь видит понятный результат доставки, не погружаясь в технические детали webhooks.

Задачи:

- На странице рассылки/отправки показать блок доставки.
- Минимальные показатели:
  - отправлено;
  - принято провайдером;
  - доставлено;
  - временные ошибки;
  - постоянные ошибки;
  - жалобы;
  - отклонено.
- Для pending/ещё не пришедших событий показывать нейтральный текст, например «Ожидаем статус доставки».
- Для fake provider можно добавить dev-подсказку, где сгенерировать событие, но не смешивать её с обычным клиентским интерфейсом.
- Не показывать клиенту внутренний `providerMessageId` на обычной странице.

Acceptance criteria:

- После webhook UI отражает новый delivery summary.
- Клиент видит русские понятные подписи.
- Технические поля доступны только admin/dev, если вообще показываются.

### T8-11. Покрыть unit-тестами provider event processing

**Цель:** зафиксировать бизнес-правила webhooks и идемпотентности.

Минимальные unit-тесты:

- Парсинг fake provider webhook.
- Маппинг event type provider → domain.
- Hard bounce меняет delivery-status и создаёт client suppression.
- Complaint создаёт `GlobalSuppression`.
- Unknown event безопасно логируется и не меняет delivery-status.
- Повторный webhook с тем же `ProviderEventId` не меняет статистику повторно.
- Запоздалый `accepted` не перетирает финальный `delivered`/`hard_bounce`/`complaint`.
- Несопоставленный webhook сохраняется/логируется как unmatched и не создаёт получателя.

Acceptance criteria:

- Unit-тесты не требуют реального провайдера.
- Тесты проверяют не только happy path, но и повторные/unknown/unmatched события.

### T8-12. Покрыть интеграционными и ручными тестами end-to-end flow

**Цель:** проверить, что webhooks работают в реальном MVP-сценарии.

Минимальные интеграционные тесты:

- Отправка → fake provider accepted webhook.
- Отправка → delivered webhook → статистика обновилась.
- Отправка → hard bounce webhook → client suppression создан.
- Отправка → complaint webhook → `GlobalSuppression` создан.
- Повторный webhook не удваивает события и счётчики.
- Следующая рассылка учитывает client suppression после hard bounce.
- Следующая рассылка любого клиента учитывает global suppression после complaint.

Ручные тесты:

- Через dev-страницу отправить fake delivery event.
- Отправить fake soft bounce.
- Отправить fake hard bounce.
- Отправить fake complaint.
- Проверить статистику рассылки.
- Проверить, что complaint email больше не получает писем.
- Проверить, что hard bounce email не получает письма от того же клиента, но не блокирует другого клиента.
- Повторить тот же webhook и убедиться, что счётчики не увеличились повторно.

## 5. Definition of Done Sprint 8

Sprint 8 считается завершённым, если:

- webhook endpoint для fake provider реализован и подключён;
- provider webhook payload парсится через adapter/parser, а не прямо в endpoint;
- события `accepted`, `delivered`, `soft_bounce`, `hard_bounce`, `complaint`, `rejected` поддержаны;
- provider events сохраняются устойчиво и идемпотентно;
- `hard_bounce` создаёт client-level suppression;
- `complaint` создаёт/подтверждает `GlobalSuppression`;
- статистика рассылки обновляется после webhooks;
- пользователь видит понятные статусы доставки, ошибок и жалоб;
- fake webhook sender/dev-инструмент позволяет проверить все события;
- повторные webhooks не дублируют события, suppression и счётчики;
- unit, integration и manual тесты из этого файла пройдены или явно отмечены как deferred с причиной.

## 6. Что не входит в Sprint 8

- Подключение реального email-провайдера.
- Реальная валидация подписей конкретного внешнего провайдера, если provider ещё не выбран.
- DKIM/SPF/DMARC.
- Tracking pixel и открытие писем.
- Click tracking.
- Ответы получателей и пересылка клиенту — это Sprint 9.
- UI аналитики уровня маркетинговой платформы.
- Автоматический retry/re-send после bounce.

## 7. Документы и разделы для синхронизации

После реализации Sprint 8 потребуется синхронизировать:

- `docs/sprints.md` — отметить фактический результат Sprint 8, если задача будет закрыта;
- `docs/platform_tz.md` — разделы про email provider adapter, webhooks, delivery/bounce/complaint, suppression/blocklist и audit log;
- `docs/specification.md`, раздел C «Юридические аспекты» — если complaint flow будет трактоваться как отдельное основание для глобальной отписки/блокировки;
- `source/tasks/sprint-9.md` — при планировании ответов получателей нужно учитывать уже существующие provider webhooks и delivery events.

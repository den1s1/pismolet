# Инвентаризация reply-контура перед этапом 3

Дата: 2026-06-27

Статус: результат спринта 3.0 из `docs/production_readiness_plan.md`.

## Что уже есть в коде

### Исходящие письма

Файл: `src/Pismolet.Application/Mailings/SendingServices.cs`.

`EmailMessage` уже содержит поля для ответов:

- `ReplyToAddress`;
- `ReplyToken`;
- metadata `replyPurpose=inbound_reply`.

`MailingSendService.BuildEmailMessage(...)` уже:

- генерирует reply-token через `IInboundReplyTokenService.Generate(...)`;
- строит `Reply-To` через `IInboundReplyTokenService.BuildReplyToAddress(...)`;
- передаёт `ReplyToAddress` в `EmailMessage`.

SMTP-адаптер уже ставит `Reply-To`, если адрес валиден.

### Reply token

Файл: `src/Pismolet.Application/Mailings/InboundReplyServices.cs`.

Уже есть:

- `InboundReplyTokenOptions`;
- `IInboundReplyTokenService`;
- `SignedInboundReplyTokenService`;
- `InboundReplyTokenPayload`;
- `InboundReplyTokenValidationResult`.

Текущий формат адреса:

```text
reply+<token>@<InboundReplies:Domain>
```

Default development domain:

```text
reply.localhost
```

Production-решение по плану:

```text
reply.pismolet.ru
```

Для production нужно задать:

```text
InboundReplies__Domain=reply.pismolet.ru
InboundReplies__Secret=<production secret>
```

### Matching входящего ответа

Файл: `src/Pismolet.Application/Mailings/InboundReplyServices.cs`.

Уже есть:

- `IInboundReplyMatchingService`;
- `InboundReplyMatchingService`;
- matching по token;
- fallback-извлечение token из `ToAddress` только для формата `reply+<token>@...`.

Ограничение текущего matching:

- token ищется только в `inbound.ReplyToken` или `inbound.ToAddress`;
- пока нет общего extraction из `X-Original-To`, `Delivered-To`, envelope recipient, `Cc`;
- формат `<token>@reply.pismolet.ru` пока не поддержан.

### Модель события ответа

Файл: `src/Pismolet.Domain/Mailings/ReplyModels.cs`.

Уже есть:

- `ReplyEvent`;
- `ReplySummary`;
- `ReplyProcessingStatus`;
- `ReplyBodyStorageStatus`.

Текущие статусы:

- `Received`;
- `Matched`;
- `QueuedForForward`;
- `Forwarded`;
- `Unmatched`;
- `IgnoredAutoReply`;
- `Duplicate`;
- `Failed`.

Этого достаточно для MVP-диагностики, но для admin UI позже может потребоваться более явная причина `ForwardFailed` или её отображение через `Failed + ErrorCode`.

### Processing service

Файл: `src/Pismolet.Application/Mailings/InboundReplyServices.cs`.

Уже есть:

- `IInboundReplyProcessingService`;
- `InboundReplyProcessingService`;
- дедупликация по `Provider + ProviderInboundEventId`;
- временное хранение тела ответа с TTL;
- auto-reply фильтр;
- matching token -> mailing/client/recipient;
- постановка в очередь пересылки;
- `ExecuteForwardAsync(...)`;
- cleanup устаревших body.

Ограничения:

- auto-reply detector минимальный;
- нет MIME parser для реальных `.eml`;
- нет spool reader;
- нет raw MIME hash fallback-дедупликации на уровне parser/source file.

### Queue

Файл: `src/Pismolet.Application/Mailings/InboundReplyServices.cs` и registration в `src/Pismolet.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`.

Уже есть:

- `IBackgroundReplyQueue`;
- используется `InlineMailingSendQueue` как реализация;
- `InboundReplyProcessingService` вызывает `queue.EnqueueForward(reply.Id)`.

Нужно проверить реализацию `InlineMailingSendQueue` в следующих спринтах, чтобы понять, есть ли реальная background-очередь для production или только inline/dev-поведение.

### Web endpoints

Файл: `src/Pismolet.Web/Endpoints/InboundReplyEndpoints.cs`.

Уже есть:

- `POST /webhooks/email/fake/inbound`;
- `POST /webhooks/email/{provider}/inbound`;
- dev UI `/dev/replies/fake`;
- dev cleanup `/dev/replies/cleanup`.

Это webhook/dev-контур, но не production inbound-spool.

### Fake inbound parser

Файл: `src/Pismolet.Application/Mailings/SendingServices.cs`.

`FakeEmailProviderAdapter.ParseInboundWebhookAsync(...)` уже умеет принимать JSON payload:

- `providerInboundEventId`;
- `from`;
- `to`;
- `replyToken`;
- `subject`;
- `textBody`;
- `htmlBody`;
- `headers`;
- `receivedAt`.

Это удобно для тестов processing service, но не заменяет MIME parser.

### Пересылка клиенту

Файлы:

- `src/Pismolet.Application/Mailings/SendingServices.cs` для fake provider;
- `src/Pismolet.Infrastructure/Mail/SmtpEmailProviderAdapter.cs` для SMTP.

Уже есть `IEmailProviderAdapter.ForwardReplyToClientAsync(ReplyEvent replyEvent, ...)`.

Fake provider пересылает в fake outbox.

SMTP provider формирует письмо клиенту. В следующих спринтах нужно проверить, что:

- `Reply-To` пересланного письма безопасно указывает на реального ответившего получателя или остаётся техническим адресом, если так надёжнее;
- нет loop с inbound reply-доменом;
- клиент не получает raw headers и token.

### Отчёт клиента

Файл: `src/Pismolet.Web/Endpoints/SendEndpoints.cs`.

Отчёт уже получает `ReplySummary` через `IReplyEventRepository.GetSummary(id)` и показывает:

- счётчик ответов в верхней сводке;
- блок «Ответы получателей» внутри подробного отчёта;
- ссылку на правила хранения и удаления ответов.

Для этапа 3 нужно проверить, что реальные inbound events после spool processing попадают в этот же summary.

### Регистрация зависимостей

Файл: `src/Pismolet.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`.

Уже регистрируются:

- `InboundReplyTokenOptions`;
- `InboundReplyOptions`;
- `IInboundReplyTokenService`;
- `IReplyEventRepository`;
- `IBackgroundReplyQueue`;
- `IInboundReplyMatchingService`;
- `IInboundReplyProcessingService`.

Не хватает:

- `InboundReplySpoolOptions`;
- MIME parser service;
- spool reader hosted service.

## Решения для следующих спринтов

1. Не создавать параллельную модель ответов. Использовать существующие `ReplyEvent`, `ReplySummary`, `IInboundReplyProcessingService`.
2. Не создавать новый endpoint для production inbound на MVP. Основной путь — Postfix spool reader.
3. Новый parser должен возвращать существующий `EmailProviderInboundParseResult` / `EmailProviderInboundEvent`.
4. `InboundReplyMatchingService` нужно расширять через отдельный token extractor, а не держать всю логику внутри `ExtractTokenFromAddress`.
5. Feature flag для spool reader обязателен: `InboundReplies__Enabled=false` по умолчанию.
6. Testing environment не должен запускать spool reader без явного включения.

## Открытые вопросы перед server-runbook

1. Какой фактический Postfix pipe будет писать `.eml` в spool: virtual alias, mailbox_command, transport или отдельный pipe service.
2. Будет ли envelope recipient доступен приложению через sidecar `.json`, имя файла или header `X-Original-To`.
3. Нужно ли сразу поддерживать `<token>@reply.pismolet.ru`, или для MVP достаточно `reply+<token>@reply.pismolet.ru`.
4. Нужен ли отдельный admin UI для reply events в MVP этапа 3, если dev UI уже показывает часть информации.

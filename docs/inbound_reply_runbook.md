# Runbook: inbound replies через Postfix spool

Статус: черновик для этапа 3 production readiness.

Цель: принять ответ получателя на технический reply-адрес, сохранить `.eml` в spool, обработать приложением и переслать клиенту.

## 1. Целевой поток

```text
Получатель нажимает Reply
  -> письмо приходит на reply-домен
  -> Postfix принимает письмо
  -> pipe/catch-all сохраняет raw MIME в incoming spool
  -> Pismolet Web worker переносит файл в processing
  -> MimeKit parser создаёт EmailProviderInboundEvent
  -> InboundReplyProcessingService сопоставляет token с рассылкой
  -> ReplyEvent ставится в очередь пересылки
  -> клиент получает пересланный ответ на email владельца рассылки
```

## 2. DNS

Целевой reply-домен:

```text
reply.pismolet.ru
```

Проверка MX:

```bash
dig MX reply.pismolet.ru
```

Ожидаемо: MX указывает на сервер, где Postfix принимает входящую почту.

## 3. Конфигурация приложения

Минимальные env/config значения:

```text
InboundReplies__Domain=reply.pismolet.ru
InboundReplies__Secret=<production-secret>
InboundReplies__Enabled=true
InboundReplies__SpoolPath=/var/lib/pismolet/inbound-replies
InboundReplies__PollIntervalSeconds=10
InboundReplies__MaxMessageBytes=10485760
InboundReplies__ProcessedRetentionDays=7
InboundReplies__FailedRetentionDays=30
InboundReplies__MaxFilesPerPoll=50
```

Для аварийного отключения inbound worker:

```text
InboundReplies__Enabled=false
```

После изменения env выполнить restart приложения.

## 4. Spool-директории

Создать структуру:

```bash
sudo mkdir -p /var/lib/pismolet/inbound-replies/incoming
sudo mkdir -p /var/lib/pismolet/inbound-replies/processing
sudo mkdir -p /var/lib/pismolet/inbound-replies/processed
sudo mkdir -p /var/lib/pismolet/inbound-replies/failed
```

Права зависят от фактических пользователей Postfix и приложения. Цель:

- Postfix/pipe может писать в `incoming`;
- приложение может читать `incoming`, переносить файлы во все подпапки и писать `.error` в `failed`;
- никто не должен выполнять файлы из spool.

Примерная схема:

```bash
sudo chown -R pismolet:pismolet /var/lib/pismolet/inbound-replies
sudo chmod -R 750 /var/lib/pismolet/inbound-replies
```

Если Postfix пишет от другого пользователя, добавить его в группу или настроить pipe на запись от пользователя приложения.

## 5. Postfix routing

Нужно выбрать фактический способ доставки входящих писем в spool.

Допустимые варианты:

1. `transport` + pipe service;
2. virtual alias на локальный transport;
3. отдельный mailbox command/catch-all, который сохраняет `.eml`.

Требования к результату:

- каждое входящее письмо сохраняется как отдельный `.eml` файл в `incoming`;
- имя файла уникальное;
- желательно включать queue id в имя файла;
- файл должен быть полностью записан до того, как приложение увидит его как `*.eml`.

Рекомендация: писать во временное имя, затем атомарно переименовывать в `.eml`.

Примерный контракт имени:

```text
20260627T230000Z-<postfix-queue-id>-<random>.eml
```

## 6. Проверка Postfix

Команды диагностики:

```bash
postconf -n
sudo tail -f /var/log/mail.log
```

Проверить:

- Postfix принимает домен `reply.pismolet.ru`;
- нет open relay;
- входящее письмо получает queue id;
- письмо попадает в `incoming`;
- приложение переносит его в `processing`, затем `processed` или `failed`.

## 7. Dry-run без публичного MX

До включения production MX можно вручную положить `.eml` в spool:

```bash
sudo cp test-reply.eml /var/lib/pismolet/inbound-replies/incoming/test-reply.eml
sudo chown pismolet:pismolet /var/lib/pismolet/inbound-replies/incoming/test-reply.eml
```

Проверить логи приложения:

```bash
journalctl -u pismolet -f
```

Ожидаемо:

- файл перенесён в `processing`;
- parser вызван;
- `ReplyEvent` создан или ошибка ушла в `failed`;
- при ошибке рядом есть `.error`.

## 8. Replay failed письма

Если письмо ушло в `failed`, можно replay:

```bash
sudo mv /var/lib/pismolet/inbound-replies/failed/<file>.eml /var/lib/pismolet/inbound-replies/incoming/<file>.eml
sudo rm -f /var/lib/pismolet/inbound-replies/failed/<file>.eml.error
```

Перед replay нужно понять причину `.error` и app-логов.

## 9. Retention

Приложение чистит:

- `processed` старше `InboundReplies__ProcessedRetentionDays`;
- `failed` старше `InboundReplies__FailedRetentionDays`.

Raw MIME не должен храниться дольше необходимого для диагностики.

## 10. Production smoke

После включения MX:

1. Отправить тестовую рассылку на внешний адрес.
2. Нажать Reply в почтовом клиенте.
3. Проверить `/var/log/mail.log`.
4. Проверить `incoming/processing/processed/failed`.
5. Проверить app-лог: parse/match/queued/forwarded.
6. Убедиться, что клиент получил пересланный ответ.
7. Открыть отчёт рассылки и проверить счётчик ответов.
8. Отправить auto-reply/bounce fixture и убедиться, что клиенту не ушло обычное письмо.

## 11. Rollback

Быстрое отключение приложения:

```text
InboundReplies__Enabled=false
```

После restart worker перестанет читать spool. Postfix может продолжать складывать `.eml` в `incoming`, если входящие ответы нужно сохранить до восстановления.

Быстрое отключение приёма:

- убрать MX `reply.pismolet.ru`; или
- отключить соответствующий transport/pipe в Postfix; или
- временно отклонять домен на уровне Postfix.

## 12. Известные ограничения текущего MVP

- Вложения входящих ответов не пересылаются клиенту как отдельная функция MVP.
- В ЛК нет inbox, только счётчик и статус пересылки.
- Полный token extraction из `X-Original-To` / `Delivered-To` должен быть проверен после выбора фактического Postfix pipe/sidecar-контракта.
- Admin route `/admin/replies` добавлен как endpoint, но должен быть подключён в `Program.cs` отдельным изменением, если оно ещё не в ветке.

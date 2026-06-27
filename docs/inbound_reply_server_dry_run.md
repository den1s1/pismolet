# Server dry-run: inbound replies

Статус: чеклист для спринта 3.8 перед включением публичного MX `reply.pismolet.ru`.

Цель: проверить, что приложение на сервере умеет обработать `.eml` из spool без реального входящего письма из интернета.

## 1. Предусловия

На сервере должен быть задеплоен commit не ниже:

```text
20e3bec46bca006e4c9b06a8e702544dcf91a54a
```

Локально перед deploy должны быть зелёные:

```bash
dotnet build Pismolet.sln /nr:false -m:1
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

## 2. Env/config для dry-run

Для dry-run можно включить worker без публичного MX:

```text
InboundReplies__Enabled=true
InboundReplies__Domain=reply.pismolet.ru
InboundReplies__SpoolPath=/var/lib/pismolet/inbound-replies
InboundReplies__PollIntervalSeconds=10
InboundReplies__MaxMessageBytes=10485760
InboundReplies__ProcessedRetentionDays=7
InboundReplies__FailedRetentionDays=30
InboundReplies__MaxFilesPerPoll=50
```

Также должен быть задан production secret для reply token:

```text
InboundReplies__Secret=<production-secret>
```

Если нужно быстро отключить worker:

```text
InboundReplies__Enabled=false
```

## 3. Создать spool-директории

```bash
sudo mkdir -p /var/lib/pismolet/inbound-replies/incoming
sudo mkdir -p /var/lib/pismolet/inbound-replies/processing
sudo mkdir -p /var/lib/pismolet/inbound-replies/processed
sudo mkdir -p /var/lib/pismolet/inbound-replies/failed
sudo chown -R pismolet:pismolet /var/lib/pismolet/inbound-replies
sudo chmod -R 750 /var/lib/pismolet/inbound-replies
```

Если приложение работает не под пользователем `pismolet`, заменить пользователя/группу на фактические.

## 4. Перезапустить приложение

Пример для systemd:

```bash
sudo systemctl restart pismolet
sudo systemctl status pismolet --no-pager
journalctl -u pismolet -n 100 --no-pager
```

В логах ожидается, что inbound spool reader включён и не падает.

## 5. Подготовить тестовый `.eml`

Этот dry-run проверяет parser и файловый reader. Token можно указать тестовый: если он невалидный, событие должно уйти в `unmatched`, но файл всё равно должен быть обработан и перенесён в `processed`.

```bash
sudo tee /var/lib/pismolet/inbound-replies/incoming/dry-run-reply.eml >/dev/null <<'EML'
From: Client <client@example.test>
To: reply+v1.payload.signature@reply.pismolet.ru
Subject: Re: Dry-run inbound reply
Message-Id: <dry-run-reply-1@example.test>
Date: Sat, 27 Jun 2026 22:50:00 +0000
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8

Тестовый ответ для проверки inbound reply spool.
EML
sudo chown pismolet:pismolet /var/lib/pismolet/inbound-replies/incoming/dry-run-reply.eml
```

## 6. Проверить обработку файла

В течение 10-20 секунд проверить:

```bash
sudo find /var/lib/pismolet/inbound-replies -maxdepth 2 -type f -print
journalctl -u pismolet -n 200 --no-pager | grep -i "Inbound reply"
```

Ожидаемый результат для dry-run с невалидным token:

- файл исчез из `incoming`;
- файл был временно в `processing`;
- файл оказался в `processed`;
- в app-логах есть processing status `unmatched` или похожая диагностическая запись;
- в `failed` нет нового `.eml` и `.error` для этого файла.

## 7. Проверить admin UI

Открыть:

```text
https://app.pismolet.ru/admin/replies
```

Ожидаемо:

- страница открывается для администратора;
- виден reply event;
- статус может быть `Не сопоставлен`, если token тестовый;
- не видно тела письма, raw MIME и reply token.

## 8. Проверить auto-reply fixture

```bash
sudo tee /var/lib/pismolet/inbound-replies/incoming/dry-run-autoreply.eml >/dev/null <<'EML'
From: Auto Reply <noreply@example.test>
To: reply+v1.payload.signature@reply.pismolet.ru
Subject: Automatic reply
Auto-Submitted: auto-replied
Message-Id: <dry-run-autoreply-1@example.test>
Date: Sat, 27 Jun 2026 22:55:00 +0000
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8

Automatic reply body.
EML
sudo chown pismolet:pismolet /var/lib/pismolet/inbound-replies/incoming/dry-run-autoreply.eml
```

Ожидаемо:

- файл переносится в `processed`;
- событие получает статус auto-reply ignored;
- клиенту ничего не пересылается.

## 9. Если файл ушёл в failed

Проверить причину:

```bash
sudo ls -la /var/lib/pismolet/inbound-replies/failed
sudo cat /var/lib/pismolet/inbound-replies/failed/*.error
journalctl -u pismolet -n 300 --no-pager
```

Типовые причины:

- `parse_failed` — MIME не разобран;
- `file_too_large` — превышен лимит;
- `processing_error` — ошибка application processing;
- `io_error` — права/доступ/конкурентное перемещение файла.

## 10. Replay failed файла

```bash
sudo mv /var/lib/pismolet/inbound-replies/failed/<file>.eml /var/lib/pismolet/inbound-replies/incoming/<file>.eml
sudo rm -f /var/lib/pismolet/inbound-replies/failed/<file>.eml.error
```

## 11. Критерий прохождения dry-run

Dry-run можно считать успешным, если:

- worker стартует при `InboundReplies__Enabled=true`;
- `.eml` из `incoming` переносится в `processed`;
- malformed/ошибочные письма уходят в `failed` с `.error`;
- тестовый обычный ответ создаёт reply event;
- auto-reply не ставится на пересылку клиенту;
- `/admin/replies` показывает диагностическое событие без body/raw/token;
- приложение не падает и не зацикливается.

После успешного dry-run можно переходить к настройке Postfix route/MX и production smoke по `docs/inbound_reply_runbook.md`.

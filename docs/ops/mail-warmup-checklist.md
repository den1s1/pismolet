# Mail warmup ops checklist

Документ фиксирует эксплуатационные настройки и проверки для лимитов прогрева исходящей почты.

## Статус

- Sprint: 4 - очередь отправки и лимиты прогрева.
- Кодовый статус: warmup gate подключён к batch-отправке.
- Поведение при блокировке: текущий recipient переводится в `Paused` с причиной `WarmupLimit`, рассылка переводится в `Paused`, provider не вызывается, очередь не re-enqueue-ится в hot loop.

## Базовые настройки

Глобальные настройки читаются из секции `MailWarmup`:

```text
MailWarmup:MaxPerMinute
MailWarmup:MaxPerHour
MailWarmup:MaxPerDay
MailWarmup:MinSecondsBetweenSends
```

Для systemd/env-формата:

```bash
MailWarmup__MaxPerMinute=1
MailWarmup__MaxPerHour=20
MailWarmup__MaxPerDay=50
MailWarmup__MinSecondsBetweenSends=30
```

## Доменные настройки

Domain overrides читаются из `MailWarmup:DomainLimits`.

Поддерживаются ключи с точками:

```text
MailWarmup:DomainLimits:gmail.com:MaxPerMinute
MailWarmup:DomainLimits:gmail.com:MaxPerHour
MailWarmup:DomainLimits:gmail.com:MaxPerDay
MailWarmup:DomainLimits:gmail.com:MinSecondsBetweenSends
```

И env-safe ключи, где точка в домене заменяется на подчёркивание:

```bash
MailWarmup__DomainLimits__gmail_com__MaxPerMinute=1
MailWarmup__DomainLimits__gmail_com__MaxPerHour=10
MailWarmup__DomainLimits__gmail_com__MaxPerDay=30
MailWarmup__DomainLimits__gmail_com__MinSecondsBetweenSends=300

MailWarmup__DomainLimits__yandex_ru__MaxPerMinute=1
MailWarmup__DomainLimits__mail_ru__MaxPerMinute=1
```

Внутри приложения `gmail_com` нормализуется в `gmail.com`, `yandex_ru` - в `yandex.ru`, `mail_ru` - в `mail.ru`.

## Рекомендуемый стартовый профиль

Для первого запуска собственного SMTP лучше начать консервативно:

```bash
MailWarmup__MaxPerMinute=1
MailWarmup__MaxPerHour=20
MailWarmup__MaxPerDay=50
MailWarmup__MinSecondsBetweenSends=30

MailWarmup__DomainLimits__gmail_com__MaxPerMinute=1
MailWarmup__DomainLimits__gmail_com__MaxPerHour=10
MailWarmup__DomainLimits__gmail_com__MaxPerDay=30
MailWarmup__DomainLimits__gmail_com__MinSecondsBetweenSends=300

MailWarmup__DomainLimits__yandex_ru__MaxPerMinute=1
MailWarmup__DomainLimits__yandex_ru__MaxPerHour=10
MailWarmup__DomainLimits__yandex_ru__MaxPerDay=30
MailWarmup__DomainLimits__yandex_ru__MinSecondsBetweenSends=180

MailWarmup__DomainLimits__mail_ru__MaxPerMinute=1
MailWarmup__DomainLimits__mail_ru__MaxPerHour=10
MailWarmup__DomainLimits__mail_ru__MaxPerDay=30
MailWarmup__DomainLimits__mail_ru__MinSecondsBetweenSends=180
```

Эти значения не являются финальной deliverability-стратегией. Их задача - безопасный старт без резких всплесков отправки.

## Проверка после деплоя

1. Убедиться, что приложение стартовало и EF migrations применились:

```bash
systemctl status pismolet --no-pager
journalctl -u pismolet -n 80 --no-pager
```

2. Проверить health/site:

```bash
curl -sS -o /tmp/pismolet.html -w "%{http_code}\n" https://app.pismolet.ru/
head -n 5 /tmp/pismolet.html
```

3. Запустить небольшую тестовую рассылку на 2-3 адреса одного домена.

4. Проверить ожидаемое поведение:

- первое письмо уходит provider-у;
- при достижении warmup limit следующая отправка не уходит provider-у;
- рассылка становится `Paused`;
- UI показывает сообщение про временную паузу прогрева, а не про дневной лимит;
- в логах нет hot loop с постоянным re-enqueue.

5. Проверить логи SMTP-адаптера:

- `SMTP send started`;
- `SMTP send succeeded` или `SMTP send failed`;
- `Transport` показывает `LocalSmtp` при локальном SMTP;
- в логах нет полных email-адресов, только домены.

## Что не делать

- Не повышать лимиты резко в первые дни после включения собственного SMTP.
- Не запускать большие кампании сразу после открытия порта 25.
- Не запускать ручной SQL для `AcceptedAt`/`AcceptedUtcDay`, если EF migrations уже применяются приложением.
- Не отключать старый сервер до окончания проверки web + mail сценариев на новом VPS.

## Открытые вопросы

- Автоматическое отложенное возобновление после `WarmupLimit` пока не реализовано. Сейчас безопасное поведение - поставить рассылку на паузу и не создавать hot loop.
- Финальные лимиты прогрева нужно уточнять после первых production-наблюдений по доставляемости, bounce и ответам почтовых серверов.

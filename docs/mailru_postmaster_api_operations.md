# Эксплуатация интеграции Mail.ru Postmaster API

Дата: 2026-07-09  
Контур: production  
Связанные документы:

- `docs/mailru_postmaster_api_integration_plan.md`
- `docs/mailru_postmaster_api_sprints.md`

## 1. Принцип хранения секрета

`refresh_token` Mail.ru Postmaster запрещено хранить:

- в Git-репозитории;
- в `appsettings*.json`;
- в unit-тестах с реальным значением;
- в systemd unit-файле, если он хранится в репозитории;
- в логах, audit trail, скриншотах и диагностических ответах.

Токен задаётся в production через уже используемый `EnvironmentFile` сервиса или другое серверное хранилище секретов.

## 2. Параметры

```text
MailruPostmaster__Enabled=true
MailruPostmaster__Domain=pismolet.ru
MailruPostmaster__RefreshToken=<реальный refresh_token>
MailruPostmaster__RequestTimeoutSeconds=15
MailruPostmaster__MaxRateLimitDelaySeconds=10
```

Дополнительные URL имеют безопасные значения по умолчанию и обычно не задаются:

```text
MailruPostmaster__OAuthBaseUrl=https://o2.mail.ru/
MailruPostmaster__ApiBaseUrl=https://postmaster.mail.ru/
```

## 3. Определение EnvironmentFile

На сервере:

```bash
sudo systemctl cat pismolet
```

Найти строку вида:

```text
EnvironmentFile=/путь/к/файлу
```

Добавить параметры Postmaster именно в этот файл.

После изменения проверить права. Файл должен быть доступен только root и пользователю, под которым работает сервис. Пример:

```bash
sudo chown root:pismolet /путь/к/файлу
sudo chmod 640 /путь/к/файлу
```

Имя группы необходимо заменить на фактическую группу сервиса.

## 4. Включение

После добавления параметров:

```bash
sudo systemctl restart pismolet
sudo systemctl status pismolet --no-pager
sudo journalctl -u pismolet -n 200 --no-pager
```

Проверить, что в логах отсутствуют:

- значение `refresh_token`;
- значение `access_token`;
- заголовок `Bearer` с токеном.

## 5. Диагностика

Административный endpoint:

```text
GET /admin/integrations/mailru-postmaster/diagnostics
```

Endpoint доступен только авторизованному администратору.

Возможные состояния:

- `disabled` — интеграция отключена;
- `not_configured` — интеграция включена, но отсутствует `refresh_token`;
- `ok` — API доступен и `pismolet.ru` найден среди подтверждённых доменов;
- `domain_not_registered` — API доступен, но настроенный домен не найден;
- `api_error` — не удалось получить список доменов;
- `partial_api_error` — список доменов получен, но запрос проблем завершился ошибкой.

Диагностический ответ не содержит токены.

## 6. Отключение

Для аварийного отключения API-интеграции установить:

```text
MailruPostmaster__Enabled=false
```

Затем:

```bash
sudo systemctl restart pismolet
```

Отключение интеграции не меняет SMTP-отправку и не удаляет заголовок `X-Postmaster-Msgtype`.

## 7. Ротация refresh_token

При подозрении на утечку:

1. Сгенерировать новую пару токенов в Mail.ru.
2. Заменить только `MailruPostmaster__RefreshToken` в серверном хранилище секретов.
3. Перезапустить сервис.
4. Открыть административную диагностику.
5. Убедиться, что статус `ok`.
6. Проверить отсутствие токенов в логах.

## 8. Smoke-проверка спринта PM-1

После production-настройки:

- [ ] Сервис запускается.
- [ ] Пользовательские страницы доступны.
- [ ] Отправка писем не изменилась.
- [ ] Диагностический endpoint недоступен неавторизованному пользователю.
- [ ] Администратор получает статус `ok`.
- [ ] В ответе есть `registeredDomain=true`.
- [ ] Список проблем домена получен.
- [ ] В логах нет секретов.
- [ ] При временном отключении API пользовательский сценарий продолжает работать.

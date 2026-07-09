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

Отключение интеграции не меняет SMTP-отправку и не удаляет заголовки `X-Postmaster-Msgtype` и `Precedence: bulk`.

## 7. Ротация refresh_token

При подозрении на утечку:

1. Сгенерировать новую пару токенов в Mail.ru.
2. Заменить только `MailruPostmaster__RefreshToken` в серверном хранилище секретов.
3. Перезапустить сервис.
4. Открыть административную диагностику.
5. Убедиться, что статус `ok`.
6. Проверить отсутствие токенов в логах.

## 8. Проверка технических заголовков массового письма

После выкладки отправить небольшую тестовую рассылку на контролируемый ящик Mail.ru и открыть полный исходник полученного письма.

Проверить наличие:

```text
Precedence: bulk
List-Unsubscribe: <https://app.pismolet.ru/...>
List-Unsubscribe-Post: List-Unsubscribe=One-Click
X-Postmaster-Msgtype: <идентификатор рассылки>
```

Затем проверить результаты аутентификации:

```text
spf=pass
dkim=pass
dmarc=pass
```

Наличие служебных заголовков в параметре `h=` заголовка `DKIM-Signature` не является отдельным требованием Mail.ru для принятия рассылки. Критично, чтобы требуемые заголовки присутствовали в письме, а сама DKIM-подпись успешно проверялась.

### Важное предупреждение по OpenDKIM

Не добавлять `Precedence`, `List-Unsubscribe`, `List-Unsubscribe-Post` или `X-Postmaster-Msgtype` в `OversignHeaders` только ради их появления в `h=`.

Production-проверка 2026-07-09 показала:

- исходная конфигурация `OversignHeaders From` давала `dkim=pass`;
- расширение `OversignHeaders` служебными заголовками добавило их имена в `h=`, но привело к `dkim=fail reason=signature_incorrect`;
- изменение было немедленно откачено из резервной копии;
- после отката повторная отправка снова дала `spf=pass`, `dkim=pass`, `dmarc=pass`.

Текущая рабочая конфигурация OpenDKIM должна оставаться без этого изменения:

```text
OversignHeaders         From
```

Если требуется изменить набор подписываемых заголовков, сначала изучить связку `SignHeaders`/`OversignHeaders`, проверить изменение вне production и только затем проводить отдельный контролируемый rollout. Нельзя считать появление имени заголовка в `h=` успешным результатом без фактического `dkim=pass` у получателя.

## 9. Smoke-проверка спринта PM-1

После production-настройки:

- [x] Сервис запускается.
- [x] Пользовательские страницы доступны.
- [x] Отправка писем не изменилась.
- [x] Администратор получает статус `ok`.
- [x] В ответе есть `registeredDomain=true`.
- [x] Список проблем домена получен: `troubles=[]`.
- [x] В логах нет совпадений по `refresh_token`, `access_token` и `Bearer`.
- [x] В исходнике тестовой рассылки есть `Precedence: bulk`.
- [x] В исходнике есть корректный `X-Postmaster-Msgtype`.
- [x] В исходнике есть `List-Unsubscribe` и `List-Unsubscribe-Post`.
- [x] После финального отката OpenDKIM подтверждены `spf=pass`, `dkim=pass`, `dmarc=pass`.
- [ ] Отдельно подтвердить недоступность диагностического endpoint неавторизованному пользователю.
- [ ] При плановой проверке аварийного отключения убедиться, что пользовательский сценарий продолжает работать.

## 10. Миграция хранилища PM-2

Хранилище Postmaster использует отдельный `MailruPostmasterDbContext` и отдельный снимок модели, но ту же production-базу PostgreSQL.

Перед включением фоновой синхронизации применить миграцию:

```bash
cd /opt/pismolet
set -a
. /etc/pismolet/pismolet.env
set +a

dotnet ef database update \
  --context MailruPostmasterDbContext \
  --project src/Pismolet.Infrastructure/Pismolet.Infrastructure.csproj \
  --startup-project src/Pismolet.Web/Pismolet.Web.csproj
```

Команду выполнять только после зелёной сборки и тестов актуального HEAD. Не выводить значение строки подключения или токена в журнал терминала.

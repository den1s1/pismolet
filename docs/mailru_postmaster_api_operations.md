# Эксплуатация интеграции Mail.ru Postmaster API

Дата: 2026-07-10  
Контур: production  
Связанные документы:

- `docs/mailru_postmaster_api_integration_plan.md`
- `docs/mailru_postmaster_api_sprints.md`
- `docs/mailru_postmaster_sync_architecture.md`

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
MailruPostmaster__SyncHourMoscow=6
MailruPostmaster__BackfillDays=30
MailruPostmaster__ResyncRecentDays=3
MailruPostmaster__RequestTimeoutSeconds=15
MailruPostmaster__MaxRateLimitDelaySeconds=10
```

Значения параметров фоновой синхронизации по умолчанию:

```text
SyncHourMoscow=6
BackfillDays=30
ResyncRecentDays=3
```

Допустимые пределы:

```text
SyncHourMoscow: 0..23
BackfillDays: 1..365
ResyncRecentDays: 1..30
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

Перед изменением production EnvironmentFile обязательно создать резервную копию.

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

Для аварийного отключения API-интеграции и фоновой синхронизации установить:

```text
MailruPostmaster__Enabled=false
```

Затем:

```bash
sudo systemctl restart pismolet
```

При `Enabled=false` фоновый сервис не обращается ни к Mail.ru API, ни к Postmaster storage.

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

Миграция `20260709201409_InitialMailruPostmasterStorage` применена в production 2026-07-09. Проверено наличие трёх таблиц:

```text
mailru_postmaster_domain_daily_metrics
mailru_postmaster_sync_states
mailru_postmaster_trouble_snapshots
```

Для нового контура или восстановления БД миграция применяется штатной EF-командой для `MailruPostmasterDbContext`:

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

## 11. Поведение фоновой синхронизации PM-2

Если интеграция включена и настроена, `MailruPostmasterSyncHostedService`:

1. Выполняет один запуск сразу после старта приложения.
2. После завершения итерации вычисляет следующий запуск по московскому времени.
3. Запускается далее один раз в сутки в час `SyncHourMoscow`.
4. При первом запуске загружает `BackfillDays` завершённых календарных дней.
5. При следующих запусках повторно синхронизирует последние `ResyncRecentDays` и одновременно догружает пропуски после простоя.
6. Не запрашивает текущий незавершённый день по Москве.
7. Обновляет метрики идемпотентно по ключу `Domain + Date`.
8. Использует persistent sync state для хранения последней попытки, успеха, ошибки и последней обработанной даты.
9. Не допускает параллельных запусков внутри текущего экземпляра приложения.
10. Корректно отменяет ожидание и активную итерацию при остановке приложения.

Первый запуск после каждого перезапуска выполняется намеренно. Повторная обработка безопасна благодаря идемпотентному upsert и повторной синхронизации свежего диапазона.

Ошибки Mail.ru API и ошибки Postmaster storage:

- фиксируются структурированными логами;
- завершают только текущую итерацию;
- не должны останавливать application host;
- не должны влиять на SMTP-отправку, оплату, модерацию или пользовательский кабинет.

Неожиданное исключение, вышедшее из orchestrator, дополнительно перехватывается hosted service и также не завершает host.

## 12. Ожидаемые журнальные события PM-2

При запуске фонового сервиса ожидается запись:

```text
Mail.ru Postmaster background synchronization started.
```

Для каждой итерации ожидаются записи начала и результата:

```text
Mail.ru Postmaster synchronization started.
Mail.ru Postmaster synchronization completed.
```

После итерации ожидается запись следующего времени запуска:

```text
Mail.ru Postmaster next synchronization scheduled.
```

При ошибке API ожидается структурированная запись:

```text
Mail.ru Postmaster synchronization failed.
```

В журналах не должны присутствовать:

- `refresh_token`;
- `access_token`;
- значение заголовка `Authorization`;
- значение заголовка `Bearer`;
- полный сырой ответ OAuth или Postmaster API.

## 13. Production smoke-проверка фоновой синхронизации PM-2

После выкладки проверить по одному действию за раз:

- [x] Перед изменением EnvironmentFile создана резервная копия `/etc/pismolet/pismolet.env.backup-20260710-053622`.
- [x] В EnvironmentFile заданы безопасные значения `SyncHourMoscow=6`, `BackfillDays=30`, `ResyncRecentDays=3`.
- [x] `pismolet.service` остаётся активным после первого и повторного запуска.
- [x] Пользовательские страницы и `/health` доступны; HTTPS health-check вернул `{"status":"ok"}`.
- [x] В журнале есть старт фоновой синхронизации.
- [x] Первая итерация завершилась успешно: API вернул `200`, обработан backfill `2026-06-10…2026-07-09`, сохранено 20 дней метрик, активных проблем нет.
- [x] Следующий запуск назначен на `2026-07-11 06:00 MSK` (`03:00 UTC`).
- [x] В `mailru_postmaster_sync_states` сохранены успешное состояние, `ConsecutiveFailures=0` и `LastDomainDate=2026-07-09`.
- [x] В таблице ежедневных метрик 20 строк, диапазон фактических данных `2026-06-20…2026-07-09`, дублей по `Domain + Date` нет.
- [x] Повторный запуск обработал свежий диапазон `2026-07-07…2026-07-09`; количество строк осталось 20, дублей осталось 0.
- [x] В журнале после выкладки нет записей уровня warning и выше и нет совпадений по `refresh_token`, `access_token`, `Authorization` или `Bearer`.
- [x] Обычная тестовая рассылка после выкладки успешно отправлена и письмо дошло до контролируемого адреса.

Production smoke PM-2 завершён успешно 2026-07-10. Фоновая синхронизация работает вне критического пути отправки, состояние хранится в PostgreSQL, повторные запуски идемпотентны.

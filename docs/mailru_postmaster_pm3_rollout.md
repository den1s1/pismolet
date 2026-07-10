# Production rollout PM-3 — админка доставляемости Mail.ru

Статус: внедрён; production smoke успешно завершён 2026-07-10  
Дата подготовки: 2026-07-10  
Дата production-внедрения: 2026-07-10  
Проверенный кодовый HEAD PM-3: `348351d71f8774f2c06adf5c77a7ab337855b393`  
Контур: production  
Ответственное направление: техническое

Связанные документы:

- `docs/mailru_postmaster_api_context.md`;
- `docs/production_operations.md`;
- `docs/mailru_postmaster_api_sprints.md`;
- `docs/mailru_postmaster_admin_architecture.md`;
- `docs/mailru_postmaster_api_operations.md`.

## 1. Подтверждённые проверки

- [x] Решение собирается.
- [x] Полный набор автоматических тестов зелёный.
- [x] SQLite-тесты read-модели, журнала и cooldown зелёные.
- [x] Исправлена SQLite-совместимость сортировки `DateTimeOffset`.
- [x] Миграция и snapshot добавлены согласованно.
- [x] Защищённая строка `InboundReplies:MaxFilesPerPoll` не изменена.
- [x] Секреты не добавлены в репозиторий.
- [x] Ошибка журнала изолирована от синхронизации и SMTP-отправки.

## 2. Состав выкладки

PM-3 добавляет:

- административную страницу `/admin/deliverability/mailru`;
- пункт административного меню;
- локальные метрики и графики за 7, 30 и 90 завершённых московских дней;
- отображение активных проблем SPF, DKIM и DMARC;
- предупреждение о низкой выборке;
- ручную команду `Синхронизировать сейчас`;
- ограничение частоты ручного запуска;
- постоянный журнал фоновых и ручных запусков.

Новая настройка необязательна:

```text
MailruPostmaster__ManualSyncCooldownSeconds=60
```

Без явной настройки используется значение 60 секунд. Допустимый диапазон: 10–3600 секунд.

Production EnvironmentFile находится в `/etc/pismolet/pismolet.env`. Его нельзя выполнять через `.` или `source`; правила безопасной работы описаны в `docs/production_operations.md`.

## 3. Миграция

Добавляется только новая таблица:

```text
mailru_postmaster_sync_runs
```

Миграция:

```text
20260710071500_AddMailruPostmasterSyncRunJournal
```

Миграция additive: существующие таблицы PM-2 и накопленные метрики не изменяются.

Порядок обязателен:

1. Проверить production HEAD исходников.
2. Создать резервную копию production-БД.
3. Применить миграцию `MailruPostmasterDbContext`.
4. Проверить запись миграции и наличие новой таблицы.
5. Развернуть новые бинарники.
6. Перезапустить `pismolet.service`.
7. Выполнить smoke-проверку.

Нельзя открывать новую страницу до применения миграции: read-модель журнала ожидает таблицу `mailru_postmaster_sync_runs`.

Канонические имена production-таблиц Postmaster:

```text
mailru_postmaster_domain_daily_metrics
mailru_postmaster_sync_states
mailru_postmaster_trouble_snapshots
mailru_postmaster_sync_runs
```

Не использовать предположительные имена вроде `mailru_postmaster_daily_metrics` или `mailru_postmaster_problem_snapshots`: таких таблиц в production нет.

Проверка сохранности PM-2 и числа накопленных строк метрик:

```bash
sudo -u postgres psql -d pismolet -Atc "
select
  to_regclass('public.mailru_postmaster_domain_daily_metrics'),
  to_regclass('public.mailru_postmaster_sync_states'),
  to_regclass('public.mailru_postmaster_trouble_snapshots'),
  (select count(*) from public.mailru_postmaster_domain_daily_metrics);
"
```

Перед использованием команды сверить ожидаемое число строк с текущим rollout-контекстом; перед PM-3 ожидалось `20`, и после выкладки сохранено `20`.

## 4. Штатные production-скрипты

Канонические версии серверных команд хранятся в репозитории:

```text
scripts/production/build-pismolet
scripts/production/run-tests-pismolet
scripts/production/deploy-pismolet
```

На production они устанавливаются как:

```text
/usr/local/bin/build-pismolet
/usr/local/bin/run-tests-pismolet
/usr/local/bin/deploy-pismolet
```

После обновления исходников установить или обновить команды:

```bash
cd /opt/pismolet

sudo install -o root -g root -m 0755 \
  scripts/production/build-pismolet \
  /usr/local/bin/build-pismolet

sudo install -o root -g root -m 0755 \
  scripts/production/run-tests-pismolet \
  /usr/local/bin/run-tests-pismolet

sudo install -o root -g root -m 0755 \
  scripts/production/deploy-pismolet \
  /usr/local/bin/deploy-pismolet
```

Назначение команд:

- `build-pismolet` проверяет ветку `Development`, отказывается работать при незакоммиченных изменениях, выполняет только fast-forward pull и собирает решение;
- `run-tests-pismolet` запускает тесты и принимает дополнительные аргументы `dotnet test`; без аргументов команда самодостаточна, а deploy передаёт `--no-build` после успешной сборки;
- `deploy-pismolet` выполняет сборку и тесты до остановки сервиса, публикует релиз во временный каталог, только затем переключает рабочий каталог, проверяет systemd и строгий HTTPS health-check, а при ошибке автоматически возвращает предыдущий релиз.

Канонический production health-check выполняется по адресу:

```text
https://app.pismolet.ru/health
```

Не использовать для этой проверки `https://pismolet.ru/health`: корневой домен не является приложением и может вернуть `404`. Значение по умолчанию зафиксировано в `scripts/production/deploy-pismolet` через `PISMOLET_HEALTH_URL`.

Успешный deploy сохраняет предыдущую версию в каталоге вида:

```text
/var/www/pismolet.rollback-YYYYMMDD-HHMMSS
```

После подтверждённого smoke старые rollback-каталоги удаляются отдельной осознанной операцией. Скрипт не применяет EF-миграции автоматически: backup БД, миграция и проверка схемы остаются отдельными шагами до выкладки бинарников.

## 5. Откат

При проблеме UI или ручной синхронизации:

- штатный `deploy-pismolet` автоматически возвращает предыдущий релиз при ошибке запуска или health-check;
- при ручном откате вернуть предыдущие бинарники;
- перезапустить `pismolet.service`;
- не удалять таблицу журнала в аварийном порядке.

Новая таблица не влияет на старые бинарники и может остаться в БД до отдельного планового решения.

Для аварийного отключения всей интеграции остаётся доступен:

```text
MailruPostmaster__Enabled=false
```

Отключение Postmaster не меняет SMTP-отправку.

## 6. Production smoke-чеклист

### Миграция

- [x] Резервная копия БД создана: `/var/backups/pismolet/pismolet-before-pm3-20260710-073441.dump`.
- [x] Права резервной копии БД ограничены до `600`.
- [x] Миграция `20260710071500_AddMailruPostmasterSyncRunJournal` применена.
- [x] Миграция присутствует в `__EFMigrationsHistory` соответствующего контекста.
- [x] Таблица `mailru_postmaster_sync_runs` существует.
- [x] Существующие таблицы PM-2 и 20 накопленных строк метрик сохранены.

### Сервис

- [x] Создан архив текущего приложения: `/var/backups/pismolet/pismolet-app-before-pm3-20260710-074737.tar.gz`.
- [x] Улучшенные production-скрипты установлены в `/usr/local/bin`, совпадают с версиями из Git и проходят `bash -n`.
- [x] Новые бинарники развернуты.
- [x] `pismolet.service` активен после перезапуска.
- [x] `https://app.pismolet.ru/health` возвращает `{"status":"ok"}`.
- [x] В журнале нет новых warning/error, кроме заранее объяснённых сообщений.
- [x] В журнале нет токенов и заголовков авторизации.

### Админка

- [x] Неавторизованный пользователь получает `302` и не получает содержимое `/admin/deliverability/mailru`.
- [x] Авторизованный администратор открывает страницу.
- [x] Пункт «Доставляемость Mail.ru» присутствует в меню один раз.
- [x] Отображаются домен, последняя синхронизация и последний обработанный день.
- [x] Отображаются и работают периоды 7, 30 и 90 дней.
- [x] Отображаются метрики, абсолютные значения, графики и предупреждение малой выборки.
- [x] Блок проблем SPF/DKIM/DMARC открывается без ошибки и показывает отсутствие активных проблем.
- [x] Страница использует единый административный каркас с боковым меню; пункт «Доставляемость Mail.ru» выделен как активный.

### Ручная синхронизация и журнал

- [x] Кнопка `Синхронизировать сейчас` запускает успешную синхронизацию.
- [x] После запуска появляется понятное сообщение результата.
- [x] В журнале появляется ручная успешная запись (`trigger=manual`, `status=succeeded`).
- [x] Повторный немедленный запуск ограничивается cooldown с указанием оставшегося времени.
- [x] После перезапуска приложения в журнале присутствует успешный фоновый запуск (`trigger=scheduled`).
- [x] Журнал не содержит токенов, заголовков авторизации или сырых ответов API.

### Независимость отправки

- [x] Обычная тестовая рассылка после выкладки успешно отправляется.
- [x] Письмо дошло до контролируемого адреса.

## 7. Итог production smoke

Production smoke успешно завершён 2026-07-10.

Подтверждены:

- успешный deploy с полной сборкой и тестами до остановки сервиса;
- активный `pismolet.service` и рабочий health-check;
- отсутствие новых warning/error и утечек чувствительных данных в логах;
- сохранность таблиц PM-2 и 20 строк накопленных метрик;
- корректная защита административной страницы;
- работа страницы, периодов 7/30/90 дней, метрик, графиков, абсолютных значений и блока SPF/DKIM/DMARC;
- успешная ручная синхронизация, журнал ручного и фонового запуска, cooldown;
- независимость SMTP: обычная тестовая рассылка отправлена и доставлена.

PM-3 соответствует критерию статуса `внедрён`.

## 8. Неблокирующие замечания после smoke

- [x] Страница `/admin/deliverability/mailru` приведена к единому административному каркасу с боковым меню; сборка и полный набор тестов зелёные, production-deploy успешен, `pismolet.service` активен, health-check отвечает `{"status":"ok"}`, UI подтверждён 2026-07-10. Кодовый HEAD: `038bafe955f30e3d2e2738c59f1468e39044d1dd`.
- [ ] Отдельно удалить или отключить устаревшую интеграцию Robokassa: провайдер отказался работать с проектом. Не смешивать эту задачу с PM-3; изменение затрагивает платёжный сценарий, конфигурацию и тесты.

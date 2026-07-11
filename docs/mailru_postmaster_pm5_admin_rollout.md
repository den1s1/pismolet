# PM-5 — rollout настроек и блока эксплуатационных сигналов

Статус: код готов к сборке и автоматическим тестам  
Дата: 2026-07-11  
Контур: production  
Ответственное направление: техническое

Связанные документы:

- `docs/mailru_postmaster_pm5_architecture.md`;
- `docs/mailru_postmaster_api_sprints.md`;
- `docs/mailru_postmaster_api_operations.md`.

## 1. Цель инкремента

Подключить первый полезный вертикальный срез PM-5:

- читать настройки предупреждений из конфигурации;
- вычислять сигналы только по локально сохранённым данным Mail.ru Postmaster;
- показывать результат на существующей административной странице доставляемости;
- не обращаться к Mail.ru API при открытии страницы;
- не влиять на SMTP, отправку, лимиты, оплату, модерацию и пользовательский кабинет;
- безопасно деградировать при ошибке самого PM-5.

Журнал срабатываний и внешний технический канал уведомлений в этот инкремент не входят.

## 2. Реализовано

### 2.1. Чтение настроек

Добавлен `MailruPostmasterAlertOptionsReader`.

Поддерживаются ключи:

```text
MailruPostmaster__AlertsEnabled
MailruPostmaster__AlertsObservationMode
MailruPostmaster__AlertsObservationStartedAt
MailruPostmaster__AlertsWindowDays
MailruPostmaster__AlertsMinimumMessagesForRates
MailruPostmaster__AlertsSpamCriticalCount
MailruPostmaster__AlertsComplaintsWarningCount
MailruPostmaster__AlertsComplaintsCriticalPercent
MailruPostmaster__AlertsProbablySpamWarningPercent
MailruPostmaster__AlertsProbablySpamGrowthPoints
MailruPostmaster__AlertsSyncStaleHours
MailruPostmaster__AlertsConsecutiveFailures
MailruPostmaster__AlertsNotificationCooldownHours
MailruPostmaster__AlertsNotificationsEnabled
```

Некорректные числовые значения приводятся к безопасным границам через существующий `Normalize()`.

Без явной конфигурации действует безопасное состояние:

```text
AlertsEnabled=false
AlertsObservationMode=true
AlertsNotificationsEnabled=false
```

### 2.2. Регистрация зависимостей

В `AddMailruPostmasterPersistence` зарегистрированы:

- нормализованные `MailruPostmasterAlertOptions` как singleton;
- `IMailruPostmasterAlertEvaluator` → `MailruPostmasterAlertEvaluator` как singleton.

Регистрация выполняется и для production PostgreSQL, и для тестового `InMemory`-контура.

### 2.3. Административный блок

На странице `/admin/deliverability/mailru` добавляется блок:

```text
PM-5 → эксплуатационный контроль
Сигналы доставляемости
```

Блок показывает:

- общий статус: выключено, недостаточно данных, спокойно, требует внимания или критично;
- режим наблюдения;
- предупреждение о малой выборке;
- список сработавших правил;
- уровень каждого сигнала;
- безопасное описание причины;
- период оценки;
- абсолютный объём писем;
- технический код правила;
- текущее и предыдущее непересекающиеся окна одинаковой длины;
- дату начала наблюдения, если она задана.

Все динамические значения HTML-кодируются.

### 2.4. Источник данных

При открытии страницы PM-5:

1. определяет последние завершённые московские дни;
2. строит текущее окно длиной `AlertsWindowDays`;
3. строит предыдущее непересекающееся окно такой же длины;
4. один раз читает объединённый период через существующий локальный `IMailruPostmasterDashboardReader`;
5. разделяет данные на текущее и предыдущее окна;
6. передаёт их в чистый evaluator.

Новый HTTP-запрос к Mail.ru API не выполняется.

### 2.5. Изоляция ошибок

Блок реализован отдельным middleware только для GET-запроса страницы доставляемости.

Если чтение данных, вычисление или построение блока завершается ошибкой:

- основная административная страница продолжает открываться;
- показывается нейтральное сообщение `Сигналы временно недоступны`;
- ошибка журналируется как warning без секретов и адресов получателей;
- отправка писем и синхронизация Postmaster не затрагиваются.

### 2.6. Автоматические тесты

Добавлены проверки:

- чтения environment-style ключей;
- нормализации порогов;
- разбора даты начала наблюдения;
- отображения режима наблюдения;
- отображения малой выборки;
- отображения спокойного состояния;
- отображения выключенного состояния;
- HTML-кодирования заголовков, описаний и кодов сигналов;
- идемпотентной вставки блока;
- безопасного сообщения при недоступности PM-5.

## 3. Изменённые файлы

- `src/Pismolet.Infrastructure/Postmaster/MailruPostmasterAlertOptionsReader.cs`;
- `src/Pismolet.Infrastructure/Postmaster/MailruPostmasterPersistenceServiceCollectionExtensions.cs`;
- `src/Pismolet.Web/Endpoints/AdminMailruPostmasterAlertMiddleware.cs`;
- `src/Pismolet.Web/Program.cs`;
- `tests/Pismolet.Web.Tests/MailruPostmasterAlertUiTests.cs`.

## 4. Миграции

Миграция БД не требуется.

Инкремент читает существующие таблицы PM-2/PM-3 и не создаёт новые сущности.

## 5. Начальная production-конфигурация

Для первого rollout использовать только режим наблюдения:

```text
MailruPostmaster__AlertsEnabled=true
MailruPostmaster__AlertsObservationMode=true
MailruPostmaster__AlertsObservationStartedAt=2026-07-12T00:00:00+03:00
MailruPostmaster__AlertsNotificationsEnabled=false
```

Остальные пороги на первом rollout оставить значениями по умолчанию.

Дата начала наблюдения должна быть заменена на фактическую дату включения в production.

## 6. Production smoke-чеклист

До deploy:

- [ ] актуальный `Development` получен в `/opt/pismolet`;
- [ ] рабочая копия не содержит изменений исходников;
- [ ] `build-pismolet` завершён успешно;
- [ ] `run-tests-pismolet` завершён успешно;
- [ ] миграция не требуется.

После deploy, но до включения PM-5:

- [ ] `pismolet.service` активен;
- [ ] `/health` возвращает `ok`;
- [ ] страница `/admin/deliverability/mailru` открывается;
- [ ] блок PM-5 виден в состоянии `Выключено`;
- [ ] PM-2, PM-3 и PM-4 продолжают работать;
- [ ] в свежих логах нет новых warning/error.

После включения режима наблюдения:

- [ ] сделана резервная копия `/etc/pismolet/pismolet.env`;
- [ ] добавлены только ключи `AlertsEnabled`, `AlertsObservationMode`, `AlertsObservationStartedAt`, `AlertsNotificationsEnabled`;
- [ ] EnvironmentFile не выполнялся через `source`;
- [ ] сервис перезапущен;
- [ ] `/health` возвращает `ok`;
- [ ] блок показывает пометку `Режим наблюдения`;
- [ ] блок показывает текущий статус и безопасное пояснение;
- [ ] при малой выборке есть отдельное предупреждение;
- [ ] открытие страницы не создаёт вызов Mail.ru API;
- [ ] SMTP и PM-4 не затронуты;
- [ ] в логах нет токенов, заголовков авторизации, адресов получателей и сырых ответов API.

## 7. Откат

Для функционального отката без отката кода:

```text
MailruPostmaster__AlertsEnabled=false
```

После изменения требуется перезапуск `pismolet.service`.

Отключение PM-5 не отключает PM-2, PM-3 или PM-4 и не удаляет накопленные данные.

## 8. Следующий инкремент

После успешного production smoke:

1. оставить PM-5 в режиме наблюдения;
2. накопить и вручную проверить фактические сигналы;
3. реализовать отдельное хранилище активных и закрытых срабатываний;
4. добавить идемпотентное обновление и закрытие сигналов по fingerprint;
5. только после проверки журнала проектировать технический канал уведомлений;
6. не переходить к PM-6 автоматически.

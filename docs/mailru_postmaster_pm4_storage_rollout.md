# Rollout storage PM-4

Дата: 2026-07-10  
Контур: production  
Статус: storage внедрён, синхронизация метрик конкретных рассылок отключена  
Архитектура: `docs/mailru_postmaster_pm4_architecture.md`  
Трекер: `docs/mailru_postmaster_api_sprints.md`

## 1. Объём выкладки

В production развёрнут первый технический пакет PM-4:

- единый построитель `MailruPostmasterMessageType.Build(Guid)`;
- использование общего построителя в SMTP-заголовке `X-Postmaster-Msgtype`;
- настройки отдельного контура метрик конкретных рассылок;
- сущности и storage ежедневных метрик по рассылкам;
- сущности и storage состояния синхронизации каждой рассылки;
- additive-миграция `20260710154330_AddMailruPostmasterMailingMetrics`;
- SQLite-тесты настроек, идемпотентного upsert и жизненного цикла состояния.

Кандидатная выборка рассылок, вызовы Mail.ru API по `msgtype`, повторные запросы до стабилизации и административный UI в этот rollout не входят.

## 2. Безопасное состояние feature flag

Перед миграцией проверено отсутствие параметров `MailruPostmaster__MailingMetrics*` в production EnvironmentFile.

При отсутствии явной настройки используется безопасное значение по умолчанию:

```text
MailruPostmaster__MailingMetricsEnabled=false
```

Поэтому после deploy новый контур не выполняет фоновые запросы к Mail.ru и не влияет на SMTP, кабинет, оплату, модерацию, PM-2 или PM-3.

## 3. Проверки до изменения БД

Подтверждено:

- сборка актуального кода зелёная;
- полный прогон автоматических тестов зелёный;
- точные количества пройденных тестов в этом rollout не фиксировались;
- EF-проверка вернула: `No changes have been made to the model since the last migration.`;
- рабочее дерево production checkout было очищено от временных `bin/` и `obj/` и перед deploy оставалось чистым.

## 4. Резервная копия

Перед применением миграции создан dump PostgreSQL:

```text
/var/backups/pismolet/pismolet-before-pm4-storage-20260710-160223.dump
```

Параметры:

- формат: custom (`pg_dump -Fc`);
- размер при проверке: `1.6M`;
- владелец: `postgres:postgres`;
- права: `600`.

## 5. Применение миграции

Миграция применена штатной EF-командой для отдельного `MailruPostmasterDbContext` с безопасным извлечением строки подключения из systemd EnvironmentFile как данных, без `source` и без вывода секрета.

Результат EF:

```text
Applying migration '20260710154330_AddMailruPostmasterMailingMetrics'.
Done.
```

Наличие миграции подтверждено в `__EFMigrationsHistory`:

```text
20260710154330_AddMailruPostmasterMailingMetrics
```

Созданы таблицы:

```text
mailru_postmaster_mailing_daily_metrics
mailru_postmaster_mailing_sync_states
```

## 6. Production deploy и smoke

Штатный `deploy-pismolet` завершился успешно по сообщению оператора. Полный вывод deploy в чат не передавался, поэтому точные внутренние шаги и длительность не фиксируются.

После deploy подтверждено:

```json
{"status":"ok"}
```

Состояние systemd:

```text
pismolet.service: active (running)
```

Проверка журналов с момента запуска нового процесса:

```text
warning..alert: -- No entries --
secret_markers=0
```

Первоначальный текстовый поиск дал пять ложных совпадений по именам SQL-колонок `ErrorCode`, `LastErrorCode` и `ConsecutiveFailures`; записей уровня `warning` и выше не было.

## 7. Проверка безопасного отключённого режима

После deploy новые таблицы остались пустыми:

```text
mailing_daily_metrics=0
mailing_sync_states=0
```

Это ожидаемое поведение при `MailingMetricsEnabled=false` и подтверждает отсутствие самопроизвольного запуска PM-4.

## 8. Проверка неизменности PM-2/PM-3

После deploy существующая доменная синхронизация продолжила работать:

```text
latest_sync_status=succeeded
latest_sync_trigger=scheduled
latest_sync_metric_days=3
domain_daily_metrics=20
```

Следовательно, additive storage PM-4 не нарушил доменную синхронизацию и не удалил накопленную историю PM-2.

## 9. Связанные commits

- `d2ded674a7cac412f7a2fc2d186745d3cdd58ee7` — архитектура PM-4;
- `fd4388ceda11155a1fa6747046d1726f18159883` — единый построитель `msgtype`;
- `c31a81a1abde0b04feaa3c63b768a311881f668d` — тест общего построителя `msgtype`;
- `43c69c447645f458f4fc2f9de6b8e8fcb947c374` — перевод SMTP на общий построитель;
- `9dafb029665ced4c4ba8b7ef26b03221190abaca` — настройки метрик рассылок;
- `15958dc66c30834fb66a7aa2ad6bc86883fc7138` — storage метрик и состояния;
- `2e99c76a0c8027ceb959cd73d22a18dfafdd2be2` — подключение сущностей к контексту;
- `f205ede7fd65b3a5f95aeaadf93bfea125f31abd` — регистрация настроек и storage;
- `5d0ecc684cb28b5530411faf2f9e7b3c4e847a0e` — тесты storage;
- `dbbe47524ccec2edd51a1995d38be7dfa6c3d68d` — устранение предупреждений xUnit;
- `e290d8223790747e8aa0cb3aa6bdc080913819f7` — основной файл миграции;
- `425a06117fad076918dc181fbae9f7a7508bb39d` — designer миграции;
- `dda6d0266df83a78e878041742e0360fa5960681` — актуальный snapshot модели и кодовый HEAD rollout.

## 10. Следующий инкремент

Следующий этап PM-4:

1. Реализовать ограниченную и справедливую выборку рассылок-кандидатов.
2. Реализовать synchronizer `stat-list-detailed` по каноническому `msgtype`.
3. Добавить fingerprint, повторные запросы, стабилизацию, `NextAttemptAt` и завершение по возрасту.
4. Изолировать ошибки одной рассылки и обработку `429` от PM-2, SMTP и application host.
5. Добавить интеграционные тесты до включения feature flag.

До завершения этого инкремента `MailruPostmaster__MailingMetricsEnabled` должен оставаться `false`.

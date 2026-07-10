# Production rollout PM-3 — админка доставляемости Mail.ru

Статус: внедрён; production smoke и follow-up успешно завершены 2026-07-10  
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
- `docs/mailru_postmaster_api_operations.md`;
- `docs/robokassa_removal.md`.

## 1. Состав PM-3

PM-3 добавил:

- административную страницу `/admin/deliverability/mailru`;
- пункт административного меню;
- локальные метрики и графики за 7, 30 и 90 завершённых московских дней;
- отображение активных проблем SPF, DKIM и DMARC;
- предупреждение о низкой выборке;
- ручную команду `Синхронизировать сейчас`;
- cooldown ручного запуска;
- постоянный журнал фоновых и ручных запусков.

Страница читает только локальную БД и не делает Mail.ru API критической зависимостью пользовательского сценария или SMTP-отправки.

## 2. Миграция и данные

Добавлена additive-миграция:

```text
20260710071500_AddMailruPostmasterSyncRunJournal
```

Добавлена таблица:

```text
mailru_postmaster_sync_runs
```

Канонические production-таблицы Postmaster:

```text
mailru_postmaster_domain_daily_metrics
mailru_postmaster_sync_states
mailru_postmaster_trouble_snapshots
mailru_postmaster_sync_runs
```

Миграция не изменяла существующие таблицы PM-2. До и после выкладки сохранено 20 строк доменных метрик.

Резервные копии перед выкладкой:

- БД: `/var/backups/pismolet/pismolet-before-pm3-20260710-073441.dump`;
- приложение: `/var/backups/pismolet/pismolet-app-before-pm3-20260710-074737.tar.gz`.

## 3. Production smoke

Подтверждено:

- [x] Решение собрано.
- [x] Полный набор автоматических тестов зелёный.
- [x] Миграция применена и присутствует в истории соответствующего контекста.
- [x] Таблица `mailru_postmaster_sync_runs` существует.
- [x] Существующие таблицы PM-2 и 20 строк метрик сохранены.
- [x] Новые бинарники развёрнуты.
- [x] `pismolet.service` активен после перезапуска.
- [x] `https://app.pismolet.ru/health` возвращает `{"status":"ok"}`.
- [x] В свежих логах нет новых warning/error и утечек чувствительных данных.
- [x] Неавторизованный пользователь не получает содержимое административной страницы.
- [x] Авторизованный администратор открывает страницу.
- [x] Периоды 7, 30 и 90 дней работают.
- [x] Метрики, абсолютные значения, графики и предупреждение малой выборки отображаются.
- [x] Блок SPF/DKIM/DMARC открывается без ошибки.
- [x] Ручная синхронизация завершается успешно.
- [x] В журнале присутствуют успешные `manual` и `scheduled` записи.
- [x] Повторный немедленный запуск ограничивается cooldown.
- [x] Обычная тестовая рассылка успешно отправлена и доставлена.
- [x] Ошибки Postmaster не блокируют SMTP.

## 4. Follow-up после smoke

- [x] Страница `/admin/deliverability/mailru` приведена к единому административному каркасу с боковым меню; пункт «Доставляемость Mail.ru» выделяется как активный. Кодовый HEAD: `038bafe955f30e3d2e2738c59f1468e39044d1dd`.
- [x] Legacy-интеграция Robokassa удалена из runtime, DI, конфигурации, endpoint'ов и активных тестов. Пять переменных `Robokassa__*` удалены из production EnvironmentFile без вывода значений; deploy, health-check, проверка legacy URL и журналов успешны. Подробности: `docs/robokassa_removal.md`; итоговый commit документации: `d91b91727b707bd2907c2c115dbca8991a6912ee`.
- [ ] Чату «Юридические аспекты» требуется вычитать и актуализировать исторические упоминания Robokassa в `docs/legal/app_ui_legal_texts.md`. Технический чат этот документ не меняет.

## 5. Откат и эксплуатация

Штатные команды сборки, тестирования, deploy, проверки health и отката описаны в `docs/production_operations.md` и `docs/mailru_postmaster_api_operations.md`.

Аварийное отключение интеграции:

```text
MailruPostmaster__Enabled=false
```

Отключение Postmaster не меняет SMTP-отправку. Таблицу журнала не требуется удалять при откате старых бинарников.

## 6. Итог

PM-3 соответствует статусу `внедрён`. Все технические follow-up задачи после smoke закрыты.

Следующий этап трека — PM-4: статистика Mail.ru по конкретным рассылкам через `msgtype`. До отдельного решения PM-6 данные Postmaster остаются наблюдаемостью и не влияют автоматически на отправку.

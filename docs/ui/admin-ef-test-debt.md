# Технический долг: EF/Postgres-покрытие админских summary-репозиториев

## Статус

Дата фиксации: 21 июня 2026 г.

UI MVP закрыт зелёным build/test gate. Замечания по спринтам 8-12 устранены:

- reflection-hack для `/admin/campaigns` удалён;
- список кампаний использует `IAdminCampaignRepository`;
- список оплат использует `IAdminPaymentRepository`;
- профиль получателя в EF фильтрует `NormalizedEmail` до materialize;
- build зелёный;
- integration tests: 4/4;
- web tests: 97/97.

Остаточный риск не блокирует UI freeze, но должен быть закрыт отдельным техническим спринтом перед активной разработкой движка рассылок или перед инфраструктурными изменениями SMTP.

## Проблема

Основная часть web endpoint-тестов работает через InMemory persistence. Поэтому самые важные EF/Postgres-ветки админки покрыты косвенно и могут сломаться без падения UI-тестов.

Риск относится к:

```text
EfAdminMailingSummaryRepository
EfAdminRecipientRepository
EF-запросам для /admin/campaigns
EF-запросам для /admin/payments
EF-запросам для /admin/recipients/{email}
```

## Цель технического спринта

Добавить прямые repository/integration-тесты на EF/Postgres-ветки, чтобы закрепить исправления после замечаний к спринтам 8-12.

## Минимальный набор тестов

### 1. EfAdminMailingSummaryRepository

Проверить, что summary строится без загрузки полного domain-графа через `IMailingRepository.ListForOwner`.

Сценарий:

```text
1. Создать DbContext на тестовой БД.
2. Добавить users.
3. Добавить mailings.
4. Добавить import_batches.
5. Добавить mailing_message_drafts.
6. Вызвать IAdminCampaignRepository.ListSummaries().
7. Проверить subject, display subject, owner email, client name, accepted recipients, status.
8. Вызвать IAdminPaymentRepository.ListSummaries().
9. Проверить те же summary-данные для оплаты.
```

### 2. EfAdminRecipientRepository.GetProfile

Проверить, что профиль получателя корректно строится по одному email.

Сценарий:

```text
1. Создать несколько mailings.
2. Добавить accepted recipients для разных email.
3. Запросить GetProfile("target@example.com").
4. Проверить, что вернулся только target@example.com.
5. Проверить owners, mailings, accepted recipients.
6. Добавить global_suppression и проверить статус.
```

### 3. Проверка фильтра до materialize

Желательно добавить проверку через EF logging или отдельный тестовый DbCommandInterceptor.

Минимальный критерий:

```text
Запрос GetProfile(email) не должен загружать все accepted recipients из таблицы recipients.
```

Если перехват SQL окажется избыточным, допускается начать с прямого теста корректности результата и оставить SQL-interceptor как следующий шаг.

## Критерий готовности

```text
dotnet build Pismolet.sln /nr:false -m:1
Build succeeded

dotnet test Pismolet.sln --no-build
Integration: green
Web: green

Добавлены прямые тесты EF summary-репозиториев.
Тесты не используют InMemory persistence для проверяемых EF-веток.
```

## Приоритет

Средний. Не блокирует текущий UI MVP, но желательно закрыть перед движком рассылок, потому админка будет использоваться для контроля очереди, оплат, статусов, bounce и репутации клиентов.

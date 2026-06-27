# План подготовки Письмолёта к production

Дата: 2026-06-26

Статус: рабочий план перед реализацией.

## 0. Исходная архитектурная рамка

Письмолёт разворачивается как две связанные, но разные части продукта:

1. `pismolet.ru` — публичный статический сайт из `public_html/`.
2. `app.pismolet.ru` — рабочее приложение из `src/Pismolet.Web`.

Эта граница уже зафиксирована в `docs/current_architecture.md` и `docs/deploy_public_html.md`:

- публичный сайт отвечает за презентацию, тарифы, справку, SEO и публичные юридические документы;
- приложение отвечает за регистрацию, вход, импорт адресов, юридически значимые подтверждения, оплату, отправку, отчёты, отписки, delivery и админку;
- публичный сайт не должен принимать клиентские базы адресатов и не должен фиксировать подтверждения по конкретным рассылкам;
- юридически значимые события должны происходить внутри приложения и legal evidence.

Текущее состояние публичного сайта: многостраничная структура уже есть, но пользовательские CTA в основном ведут на `/contacts/`. Для production нужно связать публичную часть с приложением так, чтобы посетитель мог перейти к регистрации, входу и личному кабинету без ощущения, что сайт и приложение живут отдельно.

## 1. Этап 1. Связать публичный сайт с приложением

Статус: локально выполнен, ожидает deploy-проверки на `pismolet.ru` и `app.pismolet.ru`.

Результат локальной реализации от 2026-06-26:

- на всех публичных страницах с header/footer добавлены абсолютные ссылки на вход и регистрацию в `https://app.pismolet.ru`;
- primary CTA на главной, тарифах, how-it-works, сценарных страницах и статьях больше не ведут в `/contacts/` как путь запуска;
- рядом с основными стартовыми CTA добавлена подсказка "Уже есть аккаунт? Войти";
- контакты оставлены как канал поддержки, реквизитов, правовых вопросов и вопросов по базе;
- добавлен статический test-gate `PublicSiteAppLinksTests` для public/app контракта;
- локально пройдено: `dotnet test tests/Pismolet.Web.Tests/Pismolet.Web.Tests.csproj --filter PublicSiteAppLinksTests --no-restore`;
- локально пройдено: `dotnet build Pismolet.sln /nr:false -m:1`;
- локально пройдено: `dotnet test Pismolet.sln --no-build`.

Осталось после деплоя:

- проверить опубликованные страницы на `https://pismolet.ru` на desktop и мобильной ширине около 390px;
- перейти по "Войти" и "Создать аккаунт" на `https://app.pismolet.ru`;
- проверить, что на сервере приложения задан `PISMOLET_PUBLIC_BASE_URL=https://app.pismolet.ru` или эквивалентный `App__PublicBaseUrl`;
- убедиться, что SSL и DNS для `app.pismolet.ru` работают без предупреждений браузера.

Цель: убрать остаточную модель "оставьте заявку" как основной пользовательский путь и сделать публичный сайт входной дверью в `app.pismolet.ru`, не смешивая при этом публичные документы и app-сценарии.

### 1.1. Зафиксировать URL-контракт между public и app

Принять как целевой контракт:

- публичный сайт: `https://pismolet.ru/`;
- приложение: `https://app.pismolet.ru/`;
- регистрация: `https://app.pismolet.ru/account/register`;
- вход: `https://app.pismolet.ru/account/login`;
- личный кабинет после входа: `https://app.pismolet.ru/dashboard`;
- создание рассылки для авторизованного пользователя: `https://app.pismolet.ru/mailings/new`.

Правила:

- в `public_html/` ссылки на приложение должны быть абсолютными `https://app.pismolet.ru/...`;
- не использовать относительные `/account/...` на публичном сайте, потому что на `pismolet.ru` таких маршрутов нет;
- sitemap публичного сайта не должен включать app-страницы;
- canonical публичных страниц остаётся на `https://pismolet.ru/...`;
- app-страницы юридических подтверждений остаются внутри app, публичные страницы остаются каноническими для внешнего просмотра и Robokassa.

### 1.2. Добавить постоянную навигационную связь с app

На всех публичных HTML-страницах обновить header:

- оставить текущие публичные разделы: "Как работает", "Тарифы", "Антиспам", "Статьи", "Документы", "Контакты";
- добавить отдельную группу действий справа:
  - "Войти" -> `https://app.pismolet.ru/account/login`;
  - "Создать аккаунт" или "Начать рассылку" -> `https://app.pismolet.ru/account/register`.

Важно: визуально отделить product navigation от app actions. Публичный header не должен превращаться в меню личного кабинета.

Для mobile:

- кнопки входа/регистрации должны переноситься без горизонтального скролла;
- primary action не должен вытеснять логотип и основные ссылки;
- минимально проверить 390px.

### 1.3. Заменить первичные CTA на пользовательский production-путь

Сейчас на ключевых страницах основной CTA часто ведёт на `/contacts/` с текстом "Оставить заявку". Для production это выглядит как pre-launch модель.

Заменить на:

- главная `/`: primary "Начать рассылку" -> register, secondary "Как работает" -> `/how-it-works/`;
- `/pricing/`: primary "Создать аккаунт" или "Рассчитать рассылку" -> register, secondary "Условия возврата" -> `/legal/refund/`;
- `/how-it-works/`: primary "Попробовать в личном кабинете" -> register, secondary "Тарифы" -> `/pricing/`;
- сценарные страницы `/bcc-alternative/`, `/excel-email-list/`, `/no-domain-setup/`, `/for-small-business/`, `/for-nko/`, `/for-events/`: primary -> register, secondary -> релевантная справочная/юридическая страница;
- статьи: финальный CTA -> register или pricing, а не только contacts.

Контакты оставить как support/sales/правовой канал, но не как единственный способ начать работу.

### 1.4. Добавить "уже есть аккаунт" рядом с конверсионными CTA

В hero/final CTA ключевых страниц добавить компактную вторичную ссылку:

```text
Уже есть аккаунт? Войти
```

Цель: существующий клиент не должен искать вход в ЛК через контакты или документы.

Маршрут: `https://app.pismolet.ru/account/login`.

### 1.5. Синхронизировать публичные и app-юридические ссылки

Не переносить app-чекбоксы на публичный сайт.

Публичный сайт:

- показывает оферту, правила, privacy, refund, data-processing;
- объясняет правила и тарифы;
- не фиксирует акцепты по конкретной рассылке.

Приложение:

- показывает app-legal страницы в чекбоксах;
- использует `returnUrl`;
- пишет legal evidence для регистрации, декларации базы, рекламного согласия, оплаты и запуска.

Проверить соответствие смыслов:

- публичная оферта и app `/legal/offer`;
- публичные правила рассылок и app `/legal/rules`;
- публичная privacy и app `/legal/privacy`;
- публичный data-processing и app `/legal/data-processing`;
- публичный refund и app `/legal/payment-and-refund`.

Если тексты не дословно одинаковые, это допустимо только при одинаковом смысле, версии и `document_key` там, где подключается legal evidence.

### 1.6. Обновить тексты, которые сейчас звучат как pre-launch

Проверить и заменить на production-формулировки:

- "Оставить заявку";
- "Связаться" как primary CTA;
- "обсудим первые подключения";
- "первые отправки" там, где речь должна идти о самостоятельном сценарии в ЛК.

Допустимые варианты:

- "Создать аккаунт";
- "Начать рассылку";
- "Перейти в личный кабинет";
- "Рассчитать рассылку в ЛК";
- "Войти".

Контакты при этом оставить для поддержки, вопросов по базе, возвратов, жалоб и Robokassa/документов.

### 1.7. Обновить footer

На публичных страницах в footer добавить минимум:

- "Тарифы";
- "Документы";
- "Контакты";
- "Войти" -> app login;
- "Создать аккаунт" -> app register.

Footer может быть чуть менее конверсионным, чем header, но должен давать вход в app с любой страницы.

### 1.8. Проверить app-сторону связи обратно

В приложении уже есть маршруты:

- `/account/register`;
- `/account/login`;
- `/dashboard`;
- `/mailings/new`.

Проверить отдельно:

- на app-странице входа/регистрации пользователь понимает, что он находится в приложении Письмолёта;
- из app есть понятный путь к публичным документам/контактам, если пользователь пришёл из публичного сайта;
- logout остаётся внутри app и не ломает сценарий;
- `App:PublicBaseUrl` задан как `https://app.pismolet.ru`, чтобы Robokassa, unsubscribe, tracking и служебные ссылки не собирались с неправильным доменом.

### 1.9. Тесты и автоматические проверки для этапа 1

Добавить lightweight tests/checks:

- `PublicSiteAppLinksTests`: прочитать `public_html/**/*.html` и `public_html/**/*.htm`, чтобы покрыть главную `public_html/index.htm`, и проверить, что ключевые страницы содержат app login/register ссылки;
- проверить отсутствие относительных public-ссылок вида `href="/account/login"` и `href="/account/register"`;
- проверить, что `sitemap.xml` не содержит `app.pismolet.ru`;
- проверить, что public HTML не содержит стоп-слова pre-launch: `заглушка`, `placeholder`, `soon`, `MVP`, `готовится к запуску`;
- проверить, что canonical публичных страниц остаётся на `https://pismolet.ru/...`.

Для ручной проверки:

- открыть `/`, `/pricing/`, `/how-it-works/`, `/contacts/`, одну статью и одну сценарную страницу;
- проверить header/footer на desktop и 390px;
- перейти по "Создать аккаунт" и "Войти" на `app.pismolet.ru`;
- проверить, что app открывается по HTTPS и не показывает mixed-content/SSL ошибок.

### 1.10. Деплой public_html

После изменения `public_html/**`:

- пуш в `Development` должен запустить workflow `Deploy public_html to pismolet.ru`;
- проверить успешность GitHub Actions;
- открыть опубликованные страницы на `https://pismolet.ru`;
- проверить, что Timeweb не отдаёт старые кешированные assets;
- при необходимости обновить `sitemap.xml` только для публичных страниц.

### 1.11. Acceptance criteria этапа 1

Этап можно считать закрытым, когда:

- с главной публичного сайта можно создать аккаунт и войти в ЛК;
- на ключевых страницах public есть понятная связь с app;
- первичный CTA больше не ведёт в `/contacts/` как основной путь запуска;
- публичный сайт не содержит app-only форм, загрузки базы или юридически значимых чекбоксов;
- public legal и app legal не противоречат друг другу;
- `dotnet build` и `dotnet test` зелёные;
- статический audit по `public_html` не находит старые заглушки, относительные `/account/...` ссылки и app-страницы в sitemap.

## 2. Этап 2. Финальный аудит перед Robokassa

Статус: в работе; техническая проверка тестового Robokassa-контура выполнена, ожидаются финальная юридическая вычитка, сверка данных продавца с кабинетом Robokassa и сквозная тестовая оплата из ЛК.

Опорный документ: `docs/robokassa_moderation_checklist.md`.

Цель: убедиться, что публичный сайт и app-платёжный контур выглядят как готовый коммерческий сервис, а не тестовая сборка.

Работы:

1. Сверить публичные данные продавца, контакты, цены, описание услуги и условия возврата с данными в кабинете Robokassa.
2. Провести финальную юридическую вычитку публичных документов:
   - `/legal/offer/`;
   - `/legal/privacy/`;
   - `/legal/refund/`;
   - `/legal/rules/`;
   - `/legal/data-processing/`.
3. Проверить, нужна ли отдельная публичная страница согласия на обработку персональных данных пользователя сайта.
4. Проверить после деплоя:
   - главную;
   - тарифы;
   - контакты;
   - legal-раздел;
   - sitemap;
   - mobile.
5. Сверить актуальные требования Robokassa по официальным материалам перед отправкой заявки.
6. Настроить боевые параметры:
   - `Robokassa__MerchantLogin`;
   - `Robokassa__Password1`;
   - `Robokassa__Password2`;
   - `Robokassa__IsTest=false`;
   - `Robokassa__PaymentUrl=https://auth.robokassa.ru/Merchant/Index.aspx`.
7. Указать в кабинете Robokassa:
   - `ResultURL`: `https://app.pismolet.ru/payments/robokassa/result`;
   - `SuccessURL`: `https://app.pismolet.ru/payments/robokassa/success`;
   - `FailURL`: `https://app.pismolet.ru/payments/robokassa/fail`.
8. Пройти тестовый платёж и проверить серверное подтверждение оплаты.

Результат проверки от 2026-06-26:

- опубликованные страницы `pismolet.ru`, `/pricing/`, `/contacts/`, `/legal/offer/`, `/legal/privacy/`, `/legal/refund/`, `/sitemap.xml` отвечают `200 OK`;
- `https://app.pismolet.ru/health`, `/payments/robokassa/success`, `/payments/robokassa/fail` отвечают `200 OK`;
- тестовые env-параметры Robokassa заданы на сервере приложения;
- `ResultURL`, `SuccessURL`, `FailURL` внесены в кабинет Robokassa;
- локальные тесты Robokassa/payment и public/app gate прошли;
- исправлен production-угол сверки суммы: `ResultURL` теперь сравнивает `OutSum` численно, чтобы принимать эквивалентные суммы с 2 и 6 знаками после запятой.

Acceptance criteria:

- публичный сайт открыт, без заглушек и тестовых формулировок;
- тарифы совпадают с расчётом в app;
- контакты и сведения продавца совпадают с кабинетом Robokassa;
- юридические страницы доступны с public и из app;
- Robokassa callback URL доступны на `app.pismolet.ru`;
- тестовый платёж проходит полный путь до статуса оплаты.

## 3. Этап 3. Пересылка ответов получателей клиентам

Статус: детализирован к реализации; кодовую реализацию начинать только по отдельной команде.

### 3.1. Цель этапа

Сделать production-путь для ответов получателей:

1. получатель отвечает на письмо обычной кнопкой Reply;
2. письмо приходит на технический адрес вида `reply+token@...` или другой token-aware адрес;
3. приложение извлекает token, находит рассылку, клиента и адресата;
4. создаёт или обновляет `ReplyEvent`;
5. пересылает ответ клиенту на email владельца рассылки;
6. в ЛК у клиента растёт счётчик ответов, но не появляется полноценный inbox с хранением переписки.

Целевое UX-правило для MVP: клиент получает сам ответ на свою почту, а в ЛК видит только счётчик и безопасный статус пересылки.

### 3.2. Текущее состояние кода

Уже есть:

- исходящие письма получают `Reply-To` с reply-token;
- есть доменные модели для `ReplyEvent` и summary ответов;
- есть генерация и matching по token;
- есть очередь пересылки;
- есть `ForwardReplyToClientAsync` для fake provider и SMTP provider;
- отчёт рассылки уже показывает счётчик ответов и ссылку на правила хранения ответов.

Не хватает для production:

- реального inbound-транспорта для входящих писем;
- raw MIME parser для входящего письма;
- защиты от auto-reply, mailer-daemon, postmaster и delivery-loop;
- production-конфига reply-домена и адреса;
- интеграционных тестов полного пути через реальный формат входящего письма;
- эксплуатационного runbook для DNS/Postfix/IMAP и диагностики.

### 3.3. Целевой MVP-вариант inbound-транспорта

Для production-MVP выбрать один основной путь, чтобы не распылять реализацию.

Предпочтительный путь для текущей инфраструктуры:

```text
Postfix на сервере принимает почту для reply-домена -> сохраняет raw MIME в локальную spool-папку -> background worker приложения читает spool -> парсит MIME -> создаёт EmailProviderInboundEvent -> передаёт в InboundReplyProcessingService -> ставит пересылку клиенту.
```

Причины:

- уже используется собственный SMTP/Postfix-контур;
- не нужен внешний inbound-email провайдер;
- приложение не обязано принимать внешний webhook из интернета для каждого входящего письма;
- проще диагностировать: есть mail.log, spool-файлы и app-логи;
- можно сделать безопасную атомарную обработку файлов через `incoming/processing/processed/failed`.

Альтернативы оставить как fallback:

1. **IMAP-poller** — проще подключить к catch-all mailbox, но хуже контролируется, зависит от почтового ящика и UID/state.
2. **Внешний inbound provider webhook** — хорош для масштабирования, но добавляет внешнюю зависимость и отдельную проверку подписи webhook.

Для этапа 3 не реализовывать все три варианта сразу. Основной MVP — Postfix spool reader. IMAP/webhook можно оставить в архитектуре как будущие adapters.

### 3.4. Production DNS и почтовый маршрут

Нужно принять и зафиксировать домен для ответов.

Рекомендуемый вариант:

```text
reply.pismolet.ru
```

Требования:

- MX для `reply.pismolet.ru` указывает на сервер, где работает Postfix;
- Postfix принимает входящие письма для `reply.pismolet.ru`;
- приложение генерирует `Reply-To` в одном из форматов:
  - `reply+<token>@reply.pismolet.ru`, если Postfix/catch-all корректно принимает plus-addressing;
  - `<token>@reply.pismolet.ru`, если проще настроить catch-all по домену;
  - текущий формат оставить только если он уже совместим с production Postfix.

Перед реализацией проверить фактический текущий `Reply-To`:

- где строится адрес в `IInboundReplyTokenService`;
- какой домен берётся из конфигурации;
- не конфликтует ли он с основным `info@pismolet.ru`;
- попадает ли token в local-part так, чтобы его можно было достать из envelope recipient даже если почтовый клиент изменил заголовок `To`.

DNS/серверные действия:

1. добавить/проверить MX для `reply.pismolet.ru`;
2. проверить SPF/DKIM/DMARC для исходящих ответов не требуется отдельно, потому что входящие ответы только принимаются, а пересылка клиенту идёт через текущий исходящий SMTP-домен;
3. настроить Postfix virtual/transport для reply-домена;
4. убедиться, что Postfix не принимает open relay;
5. ограничить размер входящего письма, например 10 МБ или меньше, чтобы не забивать диск;
6. настроить логирование queue-id, envelope recipient и sender.

### 3.5. Postfix spool-контракт

Для MVP использовать файловый контракт между Postfix и приложением.

Рекомендуемые директории:

```text
/var/lib/pismolet/inbound-replies/incoming
/var/lib/pismolet/inbound-replies/processing
/var/lib/pismolet/inbound-replies/processed
/var/lib/pismolet/inbound-replies/failed
```

Правила:

- Postfix/pipe кладёт каждое входящее письмо отдельным `.eml` файлом в `incoming`;
- имя файла должно быть уникальным и безопасным: timestamp + queueId + random suffix;
- приложение при обработке атомарно переносит файл из `incoming` в `processing`;
- после успешной обработки переносит в `processed` или удаляет по retention-настройке;
- после ошибки переносит в `failed` и пишет причину в `.error` sidecar-файл или в app-лог;
- права на папку должны позволять Postfix писать, а приложению читать/перемещать;
- приложение не должно выполнять shell-команды на основе имени файла или заголовков письма.

Минимальные настройки приложения:

```text
InboundReplies__Enabled=true
InboundReplies__SpoolPath=/var/lib/pismolet/inbound-replies
InboundReplies__PollIntervalSeconds=10
InboundReplies__MaxMessageBytes=10485760
InboundReplies__ProcessedRetentionDays=7
InboundReplies__FailedRetentionDays=30
```

### 3.6. Компоненты приложения, которые нужно добавить

#### 3.6.1. Options

Добавить options-модель, например:

```csharp
public sealed record InboundReplySpoolOptions(
    bool Enabled,
    string SpoolPath,
    int PollIntervalSeconds,
    long MaxMessageBytes,
    int ProcessedRetentionDays,
    int FailedRetentionDays);
```

Требования:

- безопасные default-значения для Development;
- в Testing worker выключен, если тест явно не включает его;
- production path должен задаваться через env/config;
- если `Enabled=false`, worker не стартует.

#### 3.6.2. Hosted service

Добавить hosted service, например `InboundReplySpoolReaderHostedService`:

- периодически читает `incoming/*.eml`;
- берёт ограниченное число файлов за один проход;
- проверяет размер файла до чтения;
- атомарно переносит файл в `processing`;
- передаёт stream/string raw MIME в parser;
- при успехе вызывает processing service;
- при успехе переносит файл в `processed` или удаляет;
- при ошибке переносит в `failed`;
- пишет структурированные логи без полного тела письма.

Важно: service не должен падать весь при ошибке одного письма.

#### 3.6.3. Raw MIME parser

Добавить parser, например `PostfixRawMimeInboundEmailParser`.

Вход:

- raw MIME bytes/string;
- envelope recipient, если он доступен из pipe metadata или sidecar;
- queueId/source filename для диагностики.

Выход:

- `EmailProviderInboundParseResult`;
- внутри — `EmailProviderInboundEvent`.

Parser должен извлекать:

- `From`;
- envelope recipient или `To`/`Delivered-To`/`X-Original-To`;
- reply token из адреса получателя;
- `Subject`;
- `text/plain` body;
- `text/html` fallback, если plain отсутствует;
- headers dictionary;
- `Message-Id`, `In-Reply-To`, `References`, `Auto-Submitted`, `Precedence`, `X-Autoreply`, `X-Autorespond`, `Return-Path`;
- raw payload hash для дедупликации/диагностики.

Для MIME использовать существующие зависимости, если в проекте уже есть MimeKit через SMTP provider. Не писать ручной MIME parser регулярками.

#### 3.6.4. Token extraction

Token должен извлекаться преимущественно из envelope recipient/local-part, а не только из заголовка `To`.

Порядок поиска:

1. envelope recipient из pipe/sidecar;
2. `X-Original-To`;
3. `Delivered-To`;
4. `To`;
5. `Cc` только как fallback, если выше пусто.

Поддержать форматы:

```text
reply+<token>@reply.pismolet.ru
<token>@reply.pismolet.ru
```

Если token не найден:

- создать diagnostic event со статусом unmatched/ignored, если такая модель уже есть;
- не пересылать клиенту;
- не раскрывать отправителю детали.

#### 3.6.5. Auto-reply и loop protection

Не пересылать клиенту автоматические письма и системные уведомления.

Минимальные признаки автоответа/системного письма:

- `Auto-Submitted` не пустой и не `no`;
- `Precedence: bulk`, `junk`, `list`;
- `X-Autoreply`, `X-Autorespond`, `X-Auto-Response-Suppress`;
- `Return-Path: <>`;
- From/local-part: `mailer-daemon`, `postmaster`, `no-reply`, `noreply`, `donotreply`;
- subject содержит типовые bounce/autoreply признаки: `undelivered`, `delivery status notification`, `out of office`, `automatic reply`, `автоответ`.

Для таких писем:

- сохранить минимальный `ReplyEvent`/audit со статусом ignored, если модель позволяет;
- не ставить в очередь пересылки;
- не увеличивать клиентский счётчик обычных ответов, если счётчик предназначен только для human replies;
- обязательно логировать причину ignore без тела письма.

#### 3.6.6. Дедупликация

Добавить дедупликацию, чтобы один и тот же входящий ответ не пересылался клиенту несколько раз.

Ключи дедупликации:

- provider/inbound source + providerInboundEventId, если он есть;
- иначе hash от `Message-Id + From + To + Date + Subject`;
- fallback hash raw MIME.

При повторе:

- вернуть статус ignored duplicate;
- не пересылать повторно;
- записать лог `inbound_reply_duplicate_ignored`.

#### 3.6.7. Обработка и очередь пересылки

Существующий `InboundReplyProcessingService` должен получить event и:

- найти рассылку по token;
- найти клиента/owner email;
- проверить, что рассылка существует;
- создать `ReplyEvent` со статусом, пригодным для пересылки;
- поставить событие в очередь пересылки;
- после успешного `ForwardReplyToClientAsync` поставить статус `Forwarded`;
- при ошибке поставить retryable статус или `ForwardFailed`.

Если текущая модель уже делает часть этого пути, реализация этапа должна быть минимальным подключением реального inbound parser/transport к существующему processing service.

### 3.7. Что пересылать клиенту

Пересылаемое письмо клиенту должно быть безопасным и понятным.

Минимальный формат:

- тема: `Ответ на рассылку: <оригинальная тема ответа или тема рассылки>`;
- кому: email владельца рассылки;
- от: технический адрес Письмолёта;
- Reply-To: реальный email ответившего получателя, если это безопасно и корректно парсится;
- тело:
  - название/subject рассылки;
  - email ответившего получателя;
  - дата получения;
  - текст ответа;
  - служебная строка с id рассылки/ответа.

Не пересылать клиенту:

- полные raw headers;
- DKIM/SPF/Auth-Results;
- внутренние token/signature;
- вложения входящего ответа на MVP-этапе, если нет отдельной проверки безопасности.

Вложения входящих ответов для MVP:

- либо игнорировать с пометкой "вложения не пересылаются";
- либо пересылать только после отдельного решения о лимитах, типах файлов и антивирусной проверке.

### 3.8. Хранение и retention

Целевое правило MVP:

- в ЛК не показывать тело ответа;
- тело ответа может храниться временно только для пересылки и диагностики;
- после успешной пересылки тело очищается или удаляется по короткому TTL;
- в базе остаются metadata: sender hash/email normalized, mailingId, receivedAt, forward status, error summary, body cleanup status.

Нужно сверить с текущим текстом `/legal/reply-retention`.

Если фактическое поведение будет отличаться, обновить legal-текст до релиза этапа 3:

- что именно хранится;
- сколько хранится;
- когда удаляется тело;
- что видит клиент;
- что не является полноценным inbox.

### 3.9. Админская диагностика

Для production support нужен минимальный admin-view.

Проверить, есть ли уже admin-страница reply events. Если нет — добавить:

- список последних reply events;
- фильтр по mailingId/client email/status;
- поля: receivedAt, from, token status, processing status, forward status, error summary;
- кнопка/ссылка на рассылку;
- без показа полного тела ответа по умолчанию;
- raw MIME не показывать в UI.

Минимальные статусы для диагностики:

- `Received`;
- `Matched`;
- `QueuedForForward`;
- `Forwarded`;
- `ForwardFailed`;
- `IgnoredAutoReply`;
- `IgnoredDuplicate`;
- `UnmatchedToken`;
- `InvalidPayload`.

Если enum уже существует с другими названиями, не плодить параллельный enum без необходимости — расширить текущую модель.

### 3.10. Логи и наблюдаемость

Добавить структурированные логи:

- `inbound_reply_file_seen`;
- `inbound_reply_file_too_large`;
- `inbound_reply_parse_failed`;
- `inbound_reply_token_unmatched`;
- `inbound_reply_auto_ignored`;
- `inbound_reply_matched`;
- `inbound_reply_forward_queued`;
- `inbound_reply_forwarded`;
- `inbound_reply_forward_failed`;
- `inbound_reply_duplicate_ignored`.

Логи не должны содержать:

- полное тело письма;
- полные raw headers;
- token целиком, если token можно использовать как секрет;
- персональные данные сверх необходимого. Для email в логах предпочтительно hash или domain.

Метрики/счётчики на будущее:

- сколько inbound писем получено;
- сколько matched;
- сколько forwarded;
- сколько ignored auto-reply;
- сколько failed;
- среднее время от получения до пересылки.

### 3.11. Тест-план

#### Unit-тесты parser

Покрыть `.eml` fixtures:

1. обычный plain text reply;
2. html-only reply;
3. multipart/alternative;
4. русская тема и тело в UTF-8;
5. quoted-printable/base64 body;
6. reply token в `reply+token@reply.pismolet.ru`;
7. reply token в `<token>@reply.pismolet.ru`;
8. token только в `X-Original-To`;
9. отсутствие token;
10. malformed MIME;
11. письмо больше лимита;
12. auto-reply по `Auto-Submitted`;
13. bounce с `Return-Path: <>`;
14. duplicate Message-Id.

#### Application/integration-тесты

Добавить тест полного app-пути:

1. создать пользователя;
2. создать рассылку;
3. загрузить адрес;
4. сохранить письмо;
5. пройти оплату/approve в тестовом контуре;
6. запустить отправку;
7. убедиться, что исходящее письмо получило `Reply-To` с token;
8. сформировать входящий `.eml` с этим token;
9. обработать его через parser/service;
10. убедиться, что создан `ReplyEvent`;
11. убедиться, что вызван `ForwardReplyToClientAsync`;
12. убедиться, что счётчик ответов на странице отправки увеличился.

#### Smoke-тест на сервере

После deploy:

1. отправить тестовую рассылку на свой внешний адрес;
2. ответить из почтового клиента обычной кнопкой Reply;
3. проверить `/var/log/mail.log` на приём письма reply-доменом;
4. проверить app-логи на parse/match/forward;
5. убедиться, что клиент получил пересланный ответ;
6. открыть отчёт рассылки и проверить счётчик ответов;
7. отправить auto-reply/bounce fixture и убедиться, что клиенту он не ушёл.

### 3.12. Инфраструктурный runbook

Перед реализацией подготовить или обновить runbook в `docs/`:

- DNS/MX для `reply.pismolet.ru`;
- Postfix virtual/transport/pipe настройки;
- владелец и права spool-директорий;
- env-переменные приложения;
- команды проверки:
  - `dig MX reply.pismolet.ru`;
  - `postconf -n` по нужным ключам;
  - `tail -f /var/log/mail.log`;
  - проверка файлов в spool;
  - проверка app-логов;
- процедура ручного replay failed `.eml`;
- процедура временного выключения inbound worker;
- процедура очистки `processed/failed` по retention.

### 3.13. Последовательность реализации

Реализацию после отдельной команды вести маленькими шагами:

1. Проверить и зафиксировать текущий `Reply-To` формат и конфиг reply-домена.
2. Добавить `InboundReplySpoolOptions` и регистрацию конфигурации.
3. Добавить parser raw MIME на MimeKit с unit-тестами fixtures.
4. Добавить token extraction из envelope/headers.
5. Добавить auto-reply/system-mail detector.
6. Добавить дедупликацию входящих писем.
7. Добавить spool reader hosted service, выключенный по default в Testing.
8. Подключить parser к существующему `InboundReplyProcessingService`.
9. Проверить/доработать `ForwardReplyToClientAsync`, чтобы клиенту уходило понятное письмо и не было loop.
10. Доработать reply summary/status в отчёте, если счётчик не растёт после реального inbound.
11. Добавить admin диагностику reply events или расширить существующую.
12. Обновить `/legal/reply-retention`, если фактическое хранение отличается от текста.
13. Добавить integration/smoke тесты.
14. Подготовить server runbook.
15. После зелёных тестов выполнить ручной production-like тест на сервере.

### 3.14. Спринты и подэтапы реализации

Этап 3 нужно вести отдельной серией коротких технических спринтов. Каждый спринт должен завершаться зелёным `dotnet build` и релевантными тестами. Инфраструктурные изменения на сервере выполнять только после того, как соответствующий код умеет безопасно обрабатывать тестовые данные.

#### Спринт 3.0. Инвентаризация текущего reply-контура

Цель: понять, что уже работает в коде, и не продублировать существующие сущности.

Задачи:

1. Найти текущие классы/интерфейсы:
   - `IInboundReplyTokenService`;
   - `ReplyEvent`;
   - `ReplySummary`;
   - `InboundReplyProcessingService` или его фактический аналог;
   - очередь пересылки ответов;
   - repository для reply events.
2. Зафиксировать текущий формат `Reply-To`:
   - local-part;
   - домен;
   - где хранится token;
   - какие env/options управляют адресом.
3. Проверить текущий `ForwardReplyToClientAsync`:
   - fake provider;
   - SMTP provider;
   - формат письма клиенту;
   - отсутствие loop по Reply-To.
4. Проверить текущий отчёт рассылки:
   - как считается `ReplySummary`;
   - какие статусы видит клиент;
   - есть ли admin-диагностика.
5. Составить короткую заметку в этом же плане или отдельном runbook: что реально найдено и какие названия классов будут использоваться дальше.

Результат спринта:

- принято окончательное решение по формату reply-адреса;
- понятно, какие модели расширять, а какие не трогать;
- создан список точек изменения кода.

Проверка:

```bash
dotnet build Pismolet.sln /nr:false -m:1
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

#### Спринт 3.1. Конфигурация inbound reply и безопасный skeleton

Цель: добавить выключенный по умолчанию каркас inbound-контура без обработки реальной почты.

Задачи:

1. Добавить `InboundReplySpoolOptions`.
2. Зарегистрировать options в `Program.cs`/composition root.
3. Добавить default-конфиг для Development/Production без включения worker в Testing.
4. Добавить hosted service skeleton:
   - стартует только при `InboundReplies__Enabled=true`;
   - логирует старт/остановку;
   - проверяет наличие spool-директорий;
   - не читает письма до реализации parser.
5. Добавить health/log warning, если включённый worker не видит spool path.
6. Добавить unit/integration тест, что в Testing worker не стартует самопроизвольно.

Результат спринта:

- есть безопасный feature flag;
- можно задеплоить код без влияния на production;
- server env можно готовить заранее.

Проверка:

```bash
dotnet build Pismolet.sln /nr:false -m:1
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

#### Спринт 3.2. MIME parser и token extraction

Цель: научиться превращать raw `.eml` в нормализованный inbound event без записи в базу и без пересылки клиенту.

Задачи:

1. Добавить parser на MimeKit.
2. Поддержать `text/plain`, `text/html` fallback и multipart/alternative.
3. Извлекать headers, `From`, `Subject`, дату, message-id.
4. Извлекать token из:
   - envelope recipient;
   - `X-Original-To`;
   - `Delivered-To`;
   - `To`;
   - `Cc` как fallback.
5. Поддержать форматы `reply+<token>@reply.pismolet.ru` и `<token>@reply.pismolet.ru`.
6. Добавить fixtures `.eml` для обычного plain/html/multipart письма.
7. Добавить тесты на русские темы/тела, quoted-printable/base64 и malformed MIME.

Результат спринта:

- parser возвращает `EmailProviderInboundParseResult`;
- нет пересылки клиенту;
- нет записи raw MIME в UI;
- ошибки parser безопасно логируются.

Проверка:

```bash
dotnet test tests/Pismolet.Web.Tests/Pismolet.Web.Tests.csproj --filter InboundReplyParserTests --no-restore
dotnet build Pismolet.sln /nr:false -m:1
```

#### Спринт 3.3. Auto-reply, bounce и дедупликация

Цель: до подключения пересылки отфильтровать письма, которые нельзя отправлять клиенту как обычные ответы.

Задачи:

1. Добавить detector системных писем:
   - `Auto-Submitted`;
   - `Precedence`;
   - `Return-Path: <>`;
   - `X-Autoreply`/`X-Autorespond`;
   - mailer-daemon/postmaster/no-reply отправители;
   - типовые subject-признаки bounce/autoreply.
2. Добавить вычисление dedupe key:
   - provider event id;
   - `Message-Id + From + To + Date + Subject`;
   - raw MIME hash fallback.
3. Расширить repository/service, если текущая модель не умеет хранить ignored/duplicate events.
4. Добавить статусы диагностики без увеличения клиентского счётчика обычных ответов.
5. Добавить тесты fixtures для auto-reply, bounce и duplicate.

Результат спринта:

- системные письма не ставятся в очередь пересылки;
- повторная обработка одного письма не создаёт повторную пересылку;
- причины ignore видны в логах/admin-диагностике.

Проверка:

```bash
dotnet test tests/Pismolet.Web.Tests/Pismolet.Web.Tests.csproj --filter InboundReply --no-restore
dotnet build Pismolet.sln /nr:false -m:1
```

#### Спринт 3.4. Подключение processing service и очереди пересылки

Цель: соединить parser с текущей доменной обработкой ответов и очередью пересылки.

Задачи:

1. Передать нормализованный inbound event в существующий processing service.
2. Проверить matching token -> mailingId/client/recipient.
3. Создавать `ReplyEvent` для human reply.
4. Ставить ответ в очередь пересылки клиенту.
5. Обновлять статусы:
   - received/matched;
   - queued;
   - forwarded;
   - failed.
6. Проверить, что `ForwardReplyToClientAsync` отправляет понятное письмо клиенту.
7. Не пересылать raw headers и вложения входящего ответа.
8. Не создавать loop, если клиент отвечает на пересланное письмо.

Результат спринта:

- synthetic inbound event проходит до fake forward;
- SMTP forward остаётся совместимым;
- в отчёте рассылки растёт счётчик ответов.

Проверка:

```bash
dotnet test tests/Pismolet.Web.Tests/Pismolet.Web.Tests.csproj --filter Reply --no-restore
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

#### Спринт 3.5. Spool reader и файловая обработка

Цель: подключить реальный файловый inbound-транспорт без серверной настройки Postfix.

Задачи:

1. Реализовать чтение `incoming/*.eml`.
2. Добавить атомарное перемещение `incoming -> processing`.
3. Проверять размер файла до чтения.
4. Передавать parser исходный файл и metadata.
5. При успехе переносить в `processed` или удалять по настройке.
6. При ошибке переносить в `failed` и писать `.error`/лог.
7. Добавить retention cleanup для `processed` и `failed`.
8. Добавить тест на обработку временной директории со spool-структурой.

Результат спринта:

- можно положить `.eml` в локальную `incoming` и получить обработанный reply event;
- один плохой файл не останавливает worker;
- worker выключается feature flag-ом.

Проверка:

```bash
dotnet test tests/Pismolet.Web.Tests/Pismolet.Web.Tests.csproj --filter InboundReplySpool --no-restore
dotnet build Pismolet.sln /nr:false -m:1
```

#### Спринт 3.6. Админ-диагностика и отчёт клиента

Цель: сделать поддержку reply-контура наблюдаемой без раскрытия лишних персональных данных и тела ответа.

Задачи:

1. Проверить существующие admin endpoints.
2. Добавить или расширить страницу reply events.
3. Показать последние события с фильтрами:
   - mailingId;
   - client email;
   - status;
   - период.
4. Не показывать raw MIME и полный body по умолчанию.
5. В клиентском отчёте оставить только счётчик и статус пересылки.
6. Добавить тесты на доступность admin-view только admin-пользователям.
7. Проверить отсутствие тела ответа в обычном клиентском UI.

Результат спринта:

- support может понять, почему ответ не переслался;
- клиент не получает inbox внутри ЛК;
- privacy/retention модель не ломается UI-ом.

Проверка:

```bash
dotnet test tests/Pismolet.Web.Tests/Pismolet.Web.Tests.csproj --filter ReplyAdmin --no-restore
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

#### Спринт 3.7. Инфраструктурный runbook и server dry-run

Цель: подготовить серверный маршрут до включения на реальном домене.

Задачи:

1. Создать/обновить runbook в `docs/`.
2. Зафиксировать DNS/MX для `reply.pismolet.ru`.
3. Описать Postfix virtual/transport/pipe настройки.
4. Описать владельца и права spool-директорий.
5. Описать env-переменные приложения.
6. Подготовить команды проверки `dig`, `postconf`, `tail -f /var/log/mail.log`.
7. Подготовить процедуру replay failed `.eml`.
8. Подготовить процедуру аварийного отключения `InboundReplies__Enabled=false`.
9. На сервере выполнить dry-run без публичного MX: положить тестовый `.eml` в spool и проверить обработку.

Результат спринта:

- есть воспроизводимая инструкция для production;
- приложение умеет обработать файл на сервере;
- публичный MX ещё можно не переключать.

Проверка:

```bash
dotnet build Pismolet.sln /nr:false -m:1
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

#### Спринт 3.8. Production smoke и включение reply-домена

Цель: включить реальный inbound reply путь и подтвердить его на живом письме.

Задачи:

1. Включить MX/transport для `reply.pismolet.ru`.
2. Включить `InboundReplies__Enabled=true` на сервере.
3. Отправить тестовую рассылку на внешний адрес.
4. Ответить обычной кнопкой Reply.
5. Проверить Postfix mail.log.
6. Проверить spool-файлы и app-логи.
7. Проверить, что клиент получил пересланный ответ.
8. Проверить отчёт рассылки: счётчик ответов вырос.
9. Проверить auto-reply/bounce fixture: клиенту не ушло.
10. Зафиксировать результат smoke в плане или runbook.

Результат спринта:

- reply на реальное письмо доходит до клиента;
- loop/auto-reply защита проверена;
- есть инструкция отката.

Проверка:

```bash
dotnet build Pismolet.sln /nr:false -m:1
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

#### Спринт 3.9. Юридическая и retention-синхронизация

Цель: после фактической реализации привести документы и поведение к одному описанию.

Задачи:

1. Сравнить фактическое хранение тела ответа с `/legal/reply-retention`.
2. Обновить app legal text, если TTL/body cleanup отличаются.
3. Проверить, что клиентский UI не обещает inbox.
4. Проверить, что privacy/data-processing не противоречат обработке входящих ответов.
5. Проверить audit/legal evidence, если для входящих ответов нужны отдельные события.
6. Зафиксировать финальное описание в `docs/specification.md`, если решение влияет на юридический или продуктовый контур.

Результат спринта:

- legal/UI/docs синхронизированы;
- known-gap по retention закрыт;
- этап 3 готов к закрытию.

Проверка:

```bash
dotnet build Pismolet.sln /nr:false -m:1
dotnet test Pismolet.sln --no-build /nr:false -m:1
```

### 3.15. Границы MVP и что не делать в первой реализации

В MVP этапа 3 не делать:

- полноценный inbox в ЛК;
- отображение тела ответа клиенту в приложении;
- пересылку вложений входящих ответов без отдельной проверки безопасности;
- несколько inbound-транспортов одновременно;
- внешнюю inbound webhook-интеграцию, если выбран Postfix spool;
- сложную CRM-переписку внутри Письмолёта;
- автоответ клиенту от имени Письмолёта на каждый входящий reply.

### 3.16. Риски

Основные риски:

- неверный MX/transport приведёт к потере входящих ответов;
- token может потеряться, если полагаться только на `To`, а не на envelope recipient;
- auto-reply может создать loop или заспамить клиента пересылками;
- большие входящие письма могут забить диск;
- raw MIME может содержать вредоносные вложения или HTML;
- хранение тела ответа может противоречить заявленному retention;
- пересылка клиенту может ухудшить репутацию домена, если пересылать bounce/autoreply/spam без фильтра.

Меры снижения:

- начинать с ограниченного allowlist-теста на своих адресах;
- ограничить размер входящего письма;
- не пересылать вложения входящих ответов в MVP;
- фильтровать auto-reply и `Return-Path: <>`;
- хранить raw MIME только короткое время или только failed cases;
- логировать минимум персональных данных;
- добавить ручной выключатель `InboundReplies__Enabled=false`.

### 3.17. Acceptance criteria этапа 3

Этап можно считать закрытым, когда:

- DNS/MX и Postfix принимают письма на reply-домен;
- приложение получает raw MIME через выбранный inbound-транспорт;
- token извлекается из envelope recipient или надёжного fallback-header;
- обычный ответ получателя сопоставляется с рассылкой и адресатом;
- создаётся `ReplyEvent`;
- ответ пересылается клиенту на email владельца рассылки;
- auto-reply, mailer-daemon, postmaster и bounce не пересылаются клиенту как обычные ответы;
- повторная обработка одного и того же письма не создаёт повторную пересылку;
- тело ответа хранится только в пределах заявленного retention;
- в ЛК виден увеличенный счётчик ответов и безопасный статус пересылки;
- admin видит диагностический список reply events;
- есть unit-тесты parser/auto-reply/token extraction;
- есть integration-тест полного пути;
- есть server runbook;
- `dotnet build` и `dotnet test` зелёные;
- ручной smoke-тест на реальном письме прошёл.

## 4. Рекомендуемый порядок

1. Выполнить этап 1 и задеплоить public_html.
2. После этого провести этап 2, потому что Robokassa будет смотреть связку public + app.
3. Затем делать этап 3 как отдельную инфраструктурную задачу: она важна для продукта, но не должна блокировать навигационную и платёжную готовность сайта, если Robokassa не требует работающий inbound reply до активации.

# Технический долг

Дата обновления: 2026-06-28

## Контекст

Основной flow создания рассылки переведён на порядок:

1. `/mailings/new`
2. `/mailings/{id}/message`
3. `/mailings/{id}/recipients`
4. `/mailings/{id}/confirmation`
5. `/mailings/{id}/payment`

Старые compatibility-слои для `/declaration` и декларационных полей в `POST /recipients` удалены. Тесты фиксируют отсутствие старого route `/mailings/{id}/declaration` и использование постоянных endpoint-ов для `message`, `recipients`, `confirmation`.

## Открытый долг

### 1. Dashboard после смены flow

Файл: `src/Pismolet.Web/Endpoints/DashboardEndpoints.cs`.

Нужно синхронизировать карточку рассылки с новым основным flow:

- в `NextStep` сначала предлагать написать письмо, затем добавить адресатов, затем перейти к финальному подтверждению;
- заменить пользовательскую формулировку `исключены по глобальной отписке` на наружный термин `отписка через Письмолёт` или `отписка от писем через сервис`.

Причина: основной flow уже начинается с письма, а термин `глобальная отписка` не используется в наружных текстах.

Статус: изменение пытались внести через GitHub connector, но запись файла несколько раз блокировалась. Код не трогался, чтобы не добавлять обходные middleware и не ломать зелёную сборку.

### 2. Переименование message endpoint

Файл: `src/Pismolet.Web/Endpoints/MailingRichMessageFlowEndpoints.cs`.

Желательно переименовать в постоянное имя вроде `MailingMessageStepEndpoints.cs` / `MapMailingMessageStepEndpoints`, чтобы убрать временное слово `RichMessageFlow` из основного слоя.

Статус: не blocker. Существующий endpoint работает и покрыт тестами.

## Не считать долгом

- `/mailings/{id}/confirmation` — текущий основной шаг финального подтверждения, а не временная замена `/declaration`.
- `MailingRecipientStepEndpoints.cs` — текущий основной endpoint шага адресатов.
- `MailingConfirmationSubmitEndpoints.cs` — текущий основной endpoint отправки финального подтверждения.

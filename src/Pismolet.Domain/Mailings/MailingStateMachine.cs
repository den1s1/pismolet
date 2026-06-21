namespace Pismolet.Web.Domain.Mailings;

public sealed record MailingNextAction(
    string Label,
    string? Url,
    string Description,
    bool IsAvailable = true);

public static class MailingStateMachine
{
    public static IReadOnlyList<MailingStatus> OrderedStatuses { get; } = new[]
    {
        MailingStatus.Draft,
        MailingStatus.RecipientsImported,
        MailingStatus.DeclarationConfirmed,
        MailingStatus.MessagePrepared,
        MailingStatus.PaymentPending,
        MailingStatus.Paid,
        MailingStatus.PendingChecks,
        MailingStatus.ReviewRequired,
        MailingStatus.Approved,
        MailingStatus.Sending,
        MailingStatus.Sent
    };

    public static MailingNextAction GetNextAction(Mailing mailing) => mailing.Status switch
    {
        MailingStatus.Draft => new("Загрузить адреса", $"/mailings/{mailing.Id}/recipients", "Сначала добавьте список получателей."),
        MailingStatus.RecipientsImported => mailing.LastImportStats.Accepted > 0
            ? new("Подтвердить базу", $"/mailings/{mailing.Id}/declaration", "Подтвердите правомерность базы перед письмом.")
            : new("Загрузить другой список", $"/mailings/{mailing.Id}/recipients", "В последнем импорте нет адресов, принятых к отправке."),
        MailingStatus.DeclarationConfirmed => new("Подготовить письмо", $"/mailings/{mailing.Id}/message", "Заполните отправителя, тему и текст письма."),
        MailingStatus.MessagePrepared or MailingStatus.Priced or MailingStatus.PaymentPending => new("Перейти к оплате", $"/mailings/{mailing.Id}/payment", "Проверьте стоимость и пройдите fake-оплату."),
        MailingStatus.Paid => new("Запустить проверку", $"/mailings/{mailing.Id}/checks", "Перед отправкой нужна автоматическая или ручная проверка."),
        MailingStatus.PendingChecks => new("Открыть проверку", $"/mailings/{mailing.Id}/checks", "Проверка уже запущена, обновите статус на странице проверки."),
        MailingStatus.ReviewRequired => new("Ожидает модерации", "/admin/moderation", "Рассылка ждёт ручного решения администратора."),
        MailingStatus.Approved => new("Запустить отправку", $"/mailings/{mailing.Id}/send", "Рассылка одобрена и готова к отправке."),
        MailingStatus.Sending or MailingStatus.Paused or MailingStatus.Failed or MailingStatus.Sent => new("Открыть статистику", $"/mailings/{mailing.Id}/send", "Посмотрите статус отправки, доставку и ответы."),
        MailingStatus.Rejected => new("Исправить письмо", $"/mailings/{mailing.Id}/message", "Рассылка отклонена. Исправьте письмо и повторите проверку."),
        MailingStatus.Blocked => new("Действие недоступно", null, "Рассылка заблокирована администратором.", IsAvailable: false),
        _ => new("Открыть рассылку", $"/mailings/{mailing.Id}", "Проверьте текущий статус рассылки.")
    };

    public static bool CanStartSending(Mailing mailing, bool isClientBlocked, out string? error)
    {
        if (isClientBlocked)
        {
            error = "Клиент заблокирован администратором.";
            return false;
        }

        if (mailing.Status == MailingStatus.Blocked)
        {
            error = "Рассылка заблокирована администратором.";
            return false;
        }

        if (mailing.Status != MailingStatus.Approved)
        {
            error = mailing.Status switch
            {
                MailingStatus.PaymentPending or MailingStatus.Priced or MailingStatus.MessagePrepared => "Перед отправкой нужно завершить оплату.",
                MailingStatus.Paid or MailingStatus.PendingChecks or MailingStatus.ReviewRequired => "Перед отправкой нужно дождаться одобрения проверки.",
                MailingStatus.Sending => "Рассылка уже отправляется.",
                MailingStatus.Sent => "Рассылка уже отправлена.",
                MailingStatus.Rejected => "Отклонённую рассылку нельзя отправить без исправления и повторной проверки.",
                _ => "Рассылка ещё не готова к отправке. Завершите предыдущие шаги."
            };
            return false;
        }

        error = null;
        return true;
    }
}

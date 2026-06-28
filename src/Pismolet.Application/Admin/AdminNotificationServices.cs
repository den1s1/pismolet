using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Admin;

public enum AdminNotificationKind
{
    UserRegistered,
    MailingCreated,
    MailingSubmittedToModeration,
    MailingPaid
}

public sealed record AdminNotificationSettings(
    bool UserRegistered = false,
    bool MailingCreated = false,
    bool MailingSubmittedToModeration = false,
    bool MailingPaid = false)
{
    public static AdminNotificationSettings Default { get; } = new();

    public bool IsEnabled(AdminNotificationKind kind) => kind switch
    {
        AdminNotificationKind.UserRegistered => UserRegistered,
        AdminNotificationKind.MailingCreated => MailingCreated,
        AdminNotificationKind.MailingSubmittedToModeration => MailingSubmittedToModeration,
        AdminNotificationKind.MailingPaid => MailingPaid,
        _ => false
    };
}

public interface IAdminNotificationSettingsRepository
{
    AdminNotificationSettings Get(string adminEmail);

    void Save(string adminEmail, AdminNotificationSettings settings);
}

public interface IAdminNotificationService
{
    void NotifyUserRegistered(UserAccount user);

    void NotifyMailingCreated(Mailing mailing);

    void NotifyMailingSubmittedToModeration(Mailing mailing, ModerationReview review);

    void NotifyMailingPaid(Mailing mailing, Payment payment);
}

public sealed class AdminNotificationService(
    IUserRepository users,
    IAdminAccessService admins,
    IAdminNotificationSettingsRepository settings,
    IFakeMailer mailer,
    IEmailNormalizer normalizer) : IAdminNotificationService
{
    public void NotifyUserRegistered(UserAccount user)
    {
        Notify(
            AdminNotificationKind.UserRegistered,
            user.Email,
            "Письмолёт: новый пользователь",
            $"Зарегистрирован новый пользователь: {user.DisplayName} <{user.Email}>.",
            $"/admin/users/{Uri.EscapeDataString(user.Email)}");
    }

    public void NotifyMailingCreated(Mailing mailing)
    {
        Notify(
            AdminNotificationKind.MailingCreated,
            mailing.OwnerEmail,
            "Письмолёт: создана новая рассылка",
            $"Пользователь {mailing.OwnerEmail} создал новую рассылку «{mailing.Subject}».",
            $"/admin/campaigns/{mailing.Id}");
    }

    public void NotifyMailingSubmittedToModeration(Mailing mailing, ModerationReview review)
    {
        Notify(
            AdminNotificationKind.MailingSubmittedToModeration,
            mailing.OwnerEmail,
            "Письмолёт: рассылка отправлена на модерацию",
            $"Пользователь {mailing.OwnerEmail} отправил рассылку «{DisplaySubject(mailing)}» на модерацию.",
            $"/admin/moderation/{review.Id}");
    }

    public void NotifyMailingPaid(Mailing mailing, Payment payment)
    {
        Notify(
            AdminNotificationKind.MailingPaid,
            mailing.OwnerEmail,
            "Письмолёт: рассылка оплачена",
            $"Пользователь {mailing.OwnerEmail} оплатил рассылку «{DisplaySubject(mailing)}». Сумма: {payment.TotalAmount:0.##} {payment.Currency}.",
            $"/admin/payments/{mailing.Id}");
    }

    private void Notify(AdminNotificationKind kind, string actorEmail, string subject, string message, string link)
    {
        var normalizedActor = normalizer.Normalize(actorEmail);
        if (string.IsNullOrWhiteSpace(normalizedActor) || admins.IsAdminEmail(normalizedActor))
        {
            return;
        }

        var recipients = users.ListAll()
            .Where(user => admins.IsAdminEmail(user.Email))
            .Where(user => settings.Get(user.Email).IsEnabled(kind))
            .Select(user => normalizer.Normalize(user.Email))
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(email => email, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (recipients.Length == 0)
        {
            return;
        }

        var body = $"""
{message}

Пользователь: {normalizedActor}
Ссылка в админке: {link}

Это служебное уведомление для администратора Письмолёта.
""";

        foreach (var recipient in recipients)
        {
            mailer.SendAdminNotification(recipient, subject, body, link);
        }
    }

    private static string DisplaySubject(Mailing mailing) =>
        string.IsNullOrWhiteSpace(mailing.MessageDraft?.Subject)
            ? mailing.Subject
            : mailing.MessageDraft.Subject;
}

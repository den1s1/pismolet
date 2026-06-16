using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record SaveMailingMessageCommand(
    string UserEmail,
    Guid MailingId,
    string SenderName,
    string Subject,
    string Body,
    MessageType MessageType,
    RequestMetadata Request);

public sealed record MailingMessageResult(bool Ok, string Error, Mailing? Mailing)
{
    public static MailingMessageResult Success(Mailing mailing) => new(true, string.Empty, mailing);

    public static MailingMessageResult Failure(string error) => new(false, error, null);
}

public interface IMailingMessageService
{
    MailingMessageResult Save(SaveMailingMessageCommand command);
}

public sealed class MailingMessageService(
    IMailingRepository mailings,
    IEmailNormalizer emailNormalizer,
    IAuditLogger auditLogger) : IMailingMessageService
{
    public MailingMessageResult Save(SaveMailingMessageCommand command)
    {
        var userEmail = emailNormalizer.Normalize(command.UserEmail);
        var mailing = mailings.GetForOwner(command.MailingId, userEmail);
        if (mailing is null)
        {
            return MailingMessageResult.Failure("Рассылка не найдена.");
        }

        if (mailing.LastImportStats.Accepted <= 0)
        {
            return MailingMessageResult.Failure("Сначала загрузите адреса для рассылки.");
        }

        if (mailing.Declaration is null || !mailing.Declaration.IsBaseLegalityConfirmed)
        {
            return MailingMessageResult.Failure("Сначала подтвердите базу адресов.");
        }

        if (command.MessageType == MessageType.Advertising && !mailing.Declaration.IsAdvertisingConsentConfirmed)
        {
            return MailingMessageResult.Failure("Рекламное письмо нельзя сохранить без подтверждения рекламного согласия.");
        }

        MailingMessageDraft draft;
        try
        {
            draft = MailingMessageDraft.Create(command.SenderName, command.Subject, command.Body, command.MessageType, DateTimeOffset.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return MailingMessageResult.Failure(ex.Message);
        }

        var updated = mailing.WithMessageDraft(draft);
        mailings.Update(updated);

        auditLogger.Write(new AuditRecord(
            DateTimeOffset.UtcNow,
            userEmail,
            "mailing_message_saved",
            command.Request.Ip,
            command.Request.UserAgent,
            $"{{\"mailingId\":\"{mailing.Id}\",\"messageType\":\"{draft.MessageType}\"}}"));

        return MailingMessageResult.Success(updated);
    }
}

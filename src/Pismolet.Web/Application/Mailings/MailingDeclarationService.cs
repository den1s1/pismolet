using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record ConfirmMailingDeclarationCommand(
    string UserEmail,
    Guid MailingId,
    BaseSource? BaseSource,
    bool IsBaseLegalityConfirmed,
    bool IsAdvertisingConsentConfirmed,
    MessageType IntendedMessageType,
    RequestMetadata Request);

public sealed record MailingDeclarationResult(bool Ok, string Error, Mailing? Mailing)
{
    public static MailingDeclarationResult Success(Mailing mailing) => new(true, string.Empty, mailing);

    public static MailingDeclarationResult Failure(string error, Mailing? mailing = null) => new(false, error, mailing);
}

public interface IMailingDeclarationService
{
    MailingDeclarationResult Confirm(ConfirmMailingDeclarationCommand command);
}

public sealed class MailingDeclarationService(
    IMailingRepository mailings,
    IEmailNormalizer emailNormalizer,
    IAuditLogger auditLogger) : IMailingDeclarationService
{
    public MailingDeclarationResult Confirm(ConfirmMailingDeclarationCommand command)
    {
        var userEmail = emailNormalizer.Normalize(command.UserEmail);
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return MailingDeclarationResult.Failure("Пользователь не определён.");
        }

        var mailing = mailings.GetForOwner(command.MailingId, userEmail);
        if (mailing is null)
        {
            return MailingDeclarationResult.Failure("Рассылка не найдена.");
        }

        if (mailing.LastImportStats.Accepted <= 0 || mailing.Recipients.All(x => x.Status != RecipientStatus.Accepted))
        {
            return MailingDeclarationResult.Failure("Сначала загрузите адреса для рассылки.", mailing);
        }

        if (command.BaseSource is null)
        {
            return MailingDeclarationResult.Failure("Выберите источник базы.", mailing);
        }

        if (!command.IsBaseLegalityConfirmed)
        {
            return MailingDeclarationResult.Failure("Подтвердите базу адресов.", mailing);
        }

        if (command.IntendedMessageType == MessageType.Advertising && !command.IsAdvertisingConsentConfirmed)
        {
            return MailingDeclarationResult.Failure("Для рекламного письма отметьте дополнительное подтверждение.", mailing);
        }

        var declaration = new MailingDeclaration(
            mailing.Id,
            userEmail,
            command.BaseSource.Value,
            command.IsBaseLegalityConfirmed,
            command.IsAdvertisingConsentConfirmed,
            BaseDeclarationText.CurrentVersion,
            DateTimeOffset.UtcNow,
            command.Request.Ip,
            command.Request.UserAgent);

        var updated = mailing.WithDeclaration(declaration);
        mailings.Update(updated);

        auditLogger.Write(new AuditRecord(
            DateTimeOffset.UtcNow,
            userEmail,
            "mailing_declaration_confirmed",
            command.Request.Ip,
            command.Request.UserAgent,
            $"{{\"mailingId\":\"{mailing.Id}\",\"declarationVersion\":\"{declaration.DeclarationVersion}\"}}"));

        return MailingDeclarationResult.Success(updated);
    }
}

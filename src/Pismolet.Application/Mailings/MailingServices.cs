using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record CreateMailingCommand(string OwnerEmail, string Subject);

public sealed record CreateMailingResult(bool Ok, string Error, Mailing? Mailing)
{
    public static CreateMailingResult Success(Mailing mailing) => new(true, string.Empty, mailing);

    public static CreateMailingResult Failure(string error) => new(false, error, null);
}

public interface IMailingService
{
    CreateMailingResult CreateDraft(CreateMailingCommand command, RequestMetadata request);

    Mailing? GetForOwner(Guid id, string userEmail);

    IReadOnlyCollection<Mailing> ListForOwner(string userEmail);
}

public sealed class MailingService(
    IMailingRepository mailings,
    IAuditLogger auditLogger,
    IEmailNormalizer emailNormalizer) : IMailingService
{
    private const int MaxSubjectLength = 160;

    public CreateMailingResult CreateDraft(CreateMailingCommand command, RequestMetadata request)
    {
        var userEmail = emailNormalizer.Normalize(command.OwnerEmail);
        var subject = command.Subject.Trim();

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return CreateMailingResult.Failure("Пользователь не определён.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return CreateMailingResult.Failure("Укажите название рассылки.");
        }

        if (subject.Length > MaxSubjectLength)
        {
            return CreateMailingResult.Failure($"Название рассылки должно быть не длиннее {MaxSubjectLength} символов.");
        }

        var mailing = Mailing.Draft(userEmail, subject);
        if (!mailings.TryAdd(mailing))
        {
            return CreateMailingResult.Failure("Не удалось создать рассылку. Попробуйте ещё раз.");
        }

        Audit(userEmail, "mailing_created", request, $"{{\"mailingId\":\"{mailing.Id}\"}}");
        return CreateMailingResult.Success(mailing);
    }

    public Mailing? GetForOwner(Guid id, string ownerEmail) => mailings.GetForOwner(id, emailNormalizer.Normalize(ownerEmail));

    public IReadOnlyCollection<Mailing> ListForOwner(string ownerEmail) => mailings.ListForOwner(emailNormalizer.Normalize(ownerEmail));

    private void Audit(string userEmail, string eventType, RequestMetadata request, string context) => auditLogger.Write(new AuditRecord(
        CreatedAt: DateTimeOffset.UtcNow,
        User: userEmail,
        EventType: eventType,
        Ip: request.Ip,
        UserAgent: request.UserAgent,
        Context: context));
}

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
            return MailingDeclarationResult.Failure("Пользователь не определён.", null);
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

        var importBatchId = mailing.LastImportBatch?.Id;
        var declaration = new MailingDeclaration(
            mailing.Id,
            userEmail,
            command.BaseSource.Value,
            command.IsBaseLegalityConfirmed,
            command.IsAdvertisingConsentConfirmed,
            BaseDeclarationText.CurrentVersion,
            DateTimeOffset.UtcNow,
            command.Request.Ip,
            command.Request.UserAgent)
        {
            ImportBatchId = importBatchId
        };

        var updated = mailing.WithDeclaration(declaration);
        mailings.Update(updated);

        var importBatchJson = importBatchId is null ? "null" : $"\"{importBatchId.Value}\"";
        var auditContext =
            $"{{\"mailingId\":\"{mailing.Id}\",\"importBatchId\":{importBatchJson},\"declarationVersion\":\"{declaration.DeclarationVersion}\",\"baseSource\":\"{declaration.BaseSource}\",\"intendedMessageType\":\"{command.IntendedMessageType}\"}}";

        auditLogger.Write(new AuditRecord(
            DateTimeOffset.UtcNow,
            userEmail,
            "mailing_declaration_confirmed",
            command.Request.Ip,
            command.Request.UserAgent,
            auditContext));

        return MailingDeclarationResult.Success(updated);
    }
}

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

public sealed record RenderedMessagePreview(string PlainText, string UnsubscribeUrl, string ReasonBlock, string ServiceIdentifier);

public interface IMessageRenderingService
{
    RenderedMessagePreview RenderPreview(Mailing mailing);
}

public sealed class MessageRenderingService : IMessageRenderingService
{
    private const string PreviewUnsubscribeUrl = "/unsubscribe/example-token";

    public RenderedMessagePreview RenderPreview(Mailing mailing)
    {
        var serviceId = $"Служебный идентификатор рассылки: {mailing.PublicId}";
        if (mailing.MessageDraft is null)
        {
            return new RenderedMessagePreview(string.Empty, string.Empty, string.Empty, serviceId);
        }

        var source = mailing.Declaration?.BaseSource.ToRu() ?? "загруженной базы адресов";
        var reason = $"Почему вы получили это письмо: ваш адрес находится в базе «{source}», которую отправитель подтвердил перед рассылкой.";
        var plain = string.Join("\n\n", mailing.MessageDraft.Body, reason, $"Отписаться от всех рассылок через сервис: {PreviewUnsubscribeUrl}", serviceId);

        return new RenderedMessagePreview(plain, PreviewUnsubscribeUrl, reason, serviceId);
    }
}
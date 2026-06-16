using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

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

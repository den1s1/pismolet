using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed class AdminGuardedMailingSendService(
    MailingSendService inner,
    IMailingRepository mailings,
    IUserRepository users,
    IEmailNormalizer emailNormalizer) : IMailingSendService
{
    public MailingSendResult GetState(string userEmail, Guid mailingId) => inner.GetState(userEmail, mailingId);

    public MailingSendResult StartSending(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var blocked = ValidateNotBlocked(userEmail, mailingId);
        return string.IsNullOrWhiteSpace(blocked)
            ? inner.StartSending(userEmail, mailingId, request)
            : MailingSendResult.Failure(blocked, inner.GetState(userEmail, mailingId).State);
    }

    public MailingSendResult ResumeSending(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var blocked = ValidateNotBlocked(userEmail, mailingId);
        return string.IsNullOrWhiteSpace(blocked)
            ? inner.ResumeSending(userEmail, mailingId, request)
            : MailingSendResult.Failure(blocked, inner.GetState(userEmail, mailingId).State);
    }

    public Task ExecuteQueuedBatchAsync(Guid mailingId, CancellationToken cancellationToken)
    {
        var mailing = mailings.Get(mailingId);
        if (mailing is null || mailing.Status == MailingStatus.Blocked)
        {
            return Task.CompletedTask;
        }

        if (users.GetByEmail(mailing.OwnerEmail)?.Profile.IsBlocked == true)
        {
            return Task.CompletedTask;
        }

        return inner.ExecuteQueuedBatchAsync(mailingId, cancellationToken);
    }

    private string ValidateNotBlocked(string userEmail, Guid mailingId)
    {
        var normalizedUser = emailNormalizer.Normalize(userEmail);
        if (users.GetByEmail(normalizedUser)?.Profile.IsBlocked == true)
        {
            return "Клиент заблокирован администратором.";
        }

        var mailing = mailings.GetForOwner(mailingId, normalizedUser);
        if (mailing?.Status == MailingStatus.Blocked)
        {
            return "Рассылка заблокирована администратором.";
        }

        return string.Empty;
    }
}

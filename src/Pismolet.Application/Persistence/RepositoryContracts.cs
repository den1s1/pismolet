using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Moderation;

namespace Pismolet.Web.Application.Persistence;

public interface IUserRepository
{
    UserAccount? Get(string email);

    UserAccount? GetByConfirmationToken(string token);

    void Save(UserAccount user);
}

public interface IGlobalSuppressionRepository
{
    bool IsSuppressed(string normalizedEmail);

    IReadOnlySet<string> GetSuppressedSet(IEnumerable<string> normalizedEmails);

    void Add(GlobalSuppression suppression);

    GlobalSuppression? Get(string normalizedEmail);

    IReadOnlyCollection<GlobalSuppression> ListRecent(int limit);
}

public interface IMailingRepository
{
    Mailing? Get(Guid id);

    Mailing? GetByPublicId(string publicId);

    IReadOnlyCollection<Mailing> ListByOwner(string ownerEmail);

    void Save(Mailing mailing);
}

public interface IImportBatchRepository
{
    ImportBatch? Get(Guid id);

    ImportBatch? GetLatestForMailing(Guid mailingId);

    IReadOnlyCollection<ImportBatch> ListByMailing(Guid mailingId);

    void Save(ImportBatch batch);
}

public interface IRecipientRepository
{
    IReadOnlyCollection<Recipient> ListByMailing(Guid mailingId);

    IReadOnlyCollection<Recipient> ListAcceptedByMailing(Guid mailingId);

    void ReplaceForBatch(Guid mailingId, Guid importBatchId, IReadOnlyCollection<Recipient> recipients);
}

public interface IImportIssueRepository
{
    IReadOnlyCollection<ImportIssue> ListByBatch(Guid importBatchId);

    void ReplaceForBatch(Guid importBatchId, IReadOnlyCollection<ImportIssue> issues);
}

public interface IMailingDeclarationRepository
{
    MailingDeclaration? Get(Guid mailingId);

    void Save(MailingDeclaration declaration);
}

public interface IMailingMessageDraftRepository
{
    MailingMessageDraft? Get(Guid mailingId);

    void Save(MailingMessageDraft draft);
}

public interface IAuditLogger
{
    void Log(AuditRecord record);
}

public interface IModerationReviewRepository
{
    ModerationReview? Get(Guid id);

    ModerationReview? GetOpenByMailingId(Guid mailingId);

    IReadOnlyCollection<ModerationReview> ListOpen();

    void Save(ModerationReview review);
}

public interface IModerationActionLogRepository
{
    void Add(ModerationActionLog log);

    IReadOnlyCollection<ModerationActionLog> ListForReview(Guid reviewId);
}

public interface ISendEventRepository
{
    SendEvent? Get(Guid mailingId, string recipientEmail);

    SendEvent? GetByProviderMessageId(string providerMessageId);

    SendEvent? GetByTrackingToken(string trackingToken);

    IReadOnlyCollection<SendEvent> ListByMailingId(Guid mailingId);

    IReadOnlyCollection<SendEvent> GetPendingBatch(Guid mailingId, int batchSize);

    int CountAcceptedForOwnerOnUtcDate(string ownerEmail, DateOnly utcDate);

    IReadOnlyCollection<MailWarmupAcceptedSend> ListAcceptedForWarmupWindow(string ownerEmail, DateTimeOffset sinceUtc);

    void Save(SendEvent sendEvent);

    MailingSendSummary GetSummary(Guid mailingId, int totalAcceptedRecipients);
}

public interface IProviderWebhookEventRepository
{
    ProviderWebhookEvent? GetByProviderEventId(string provider, string providerEventId);

    IReadOnlyCollection<ProviderWebhookEvent> ListByMailingId(Guid mailingId);

    IReadOnlyCollection<ProviderWebhookEvent> ListRecent(int limit);

    void Save(ProviderWebhookEvent webhookEvent);
}

public interface IClientSuppressionRepository
{
    bool IsSuppressed(string clientId, string normalizedEmail);

    IReadOnlySet<string> GetSuppressedSet(string clientId, IEnumerable<string> normalizedEmails);

    IReadOnlyCollection<ClientSuppression> ListRecent(int limit);

    ClientSuppression AddOrUpdate(ClientSuppression suppression);
}

public interface IReplyEventRepository
{
    ReplyEvent AddIfNotExists(ReplyEvent replyEvent);

    ReplyEvent? Get(Guid id);

    ReplyEvent? GetByProviderEventId(string provider, string providerInboundEventId);

    ReplySummary GetSummary(Guid mailingId);

    int CountByMailing(Guid mailingId);

    ReplyEvent? GetLastByMailing(Guid mailingId);

    IReadOnlyCollection<ReplyEvent> ListRecentByMailing(Guid mailingId, int limit);

    IReadOnlyCollection<ReplyEvent> ListRecent(int limit);

    IReadOnlyCollection<ReplyEvent> FindPendingForward(DateTimeOffset now, int batchSize);

    IReadOnlyCollection<ReplyEvent> FindExpiredBodies(DateTimeOffset now, int batchSize);

    void Save(ReplyEvent replyEvent);

    void MarkForwardQueued(Guid replyEventId);

    void MarkForwarded(Guid replyEventId);

    void MarkForwardFailed(Guid replyEventId, string errorCode, string errorMessage);

    void MarkBodyDeleted(Guid replyEventId);
}

using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfReplyEventDiagnosticRepository(PismoletDbContext db) : IReplyEventRepository
{
    private readonly Pismolet.Web.Infrastructure.Persistence.EfReplyEventRepository inner = new(db);

    public ReplyEvent AddIfNotExists(ReplyEvent replyEvent) => inner.AddIfNotExists(replyEvent);

    public ReplyEvent? Get(Guid id) => inner.Get(id);

    public ReplyEvent? GetByProviderEventId(string provider, string providerInboundEventId) => inner.GetByProviderEventId(provider, providerInboundEventId);

    public ReplySummary GetSummary(Guid mailingId) => inner.GetSummary(mailingId);

    public int CountByMailing(Guid mailingId) => inner.CountByMailing(mailingId);

    public ReplyEvent? GetLastByMailing(Guid mailingId) => inner.GetLastByMailing(mailingId);

    public IReadOnlyCollection<ReplyEvent> ListRecentByMailing(Guid mailingId, int limit) => inner.ListRecentByMailing(mailingId, limit);

    public IReadOnlyCollection<ReplyEvent> ListRecent(int limit) => inner.ListRecent(limit);

    public IReadOnlyCollection<ReplyEvent> FindPendingForward(DateTimeOffset now, int batchSize) => inner.FindPendingForward(now, batchSize);

    public IReadOnlyCollection<ReplyEvent> FindExpiredBodies(DateTimeOffset now, int batchSize) => inner.FindExpiredBodies(now, batchSize);

    public void Save(ReplyEvent replyEvent) => inner.Save(replyEvent);

    public ReplyEvent? TryClaimForward(Guid replyEventId)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = db.ReplyEvents
            .Where(x =>
                x.Id == replyEventId &&
                (x.ProcessingStatus == ReplyProcessingStatus.QueuedForForward.ToString() ||
                 (x.ProcessingStatus == ReplyProcessingStatus.Failed.ToString() && x.ForwardRetryCount < 3)))
            .ExecuteUpdate(setters => setters
                .SetProperty(x => x.ProcessingStatus, ReplyProcessingStatus.Forwarding.ToString())
                .SetProperty(x => x.ForwardQueuedAt, x => x.ForwardQueuedAt ?? now)
                .SetProperty(x => x.ProcessedAt, x => x.ProcessedAt ?? now));

        return updated == 0 ? null : Get(replyEventId);
    }

    public void MarkForwardQueued(Guid replyEventId) => inner.MarkForwardQueued(replyEventId);

    public void MarkForwarded(Guid replyEventId)
    {
        if (db.ReplyEvents.FirstOrDefault(x => x.Id == replyEventId) is { } entity)
        {
            var preserveDiagnostic = entity.MailingId is null &&
                !string.IsNullOrWhiteSpace(entity.ClientId) &&
                !string.IsNullOrWhiteSpace(entity.ForwardToEmailNormalized);

            entity.ProcessingStatus = ReplyProcessingStatus.Forwarded.ToString();
            entity.ForwardedAt = DateTimeOffset.UtcNow;
            if (!preserveDiagnostic)
            {
                entity.ErrorCode = null;
                entity.ErrorMessage = null;
            }

            db.SaveChanges();
        }
    }

    public void MarkForwardFailed(Guid replyEventId, string errorCode, string errorMessage) => inner.MarkForwardFailed(replyEventId, errorCode, errorMessage);

    public void MarkBodyDeleted(Guid replyEventId) => inner.MarkBodyDeleted(replyEventId);
}

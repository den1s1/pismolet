using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfReplyEventRepository(PismoletDbContext db) : IReplyEventRepository
{
    public ReplyEvent AddIfNotExists(ReplyEvent replyEvent)
    {
        var existing = db.ReplyEvents.FirstOrDefault(x => x.Provider == replyEvent.Provider && x.ProviderInboundEventId == replyEvent.ProviderInboundEventId);
        if (existing is not null)
        {
            return ToDomain(existing);
        }

        var entity = ToEntity(replyEvent);
        db.ReplyEvents.Add(entity);
        db.SaveChanges();
        return ToDomain(entity);
    }

    public ReplyEvent? Get(Guid id) => db.ReplyEvents.AsNoTracking().FirstOrDefault(x => x.Id == id) is { } entity ? ToDomain(entity) : null;

    public ReplyEvent? GetByProviderEventId(string provider, string providerInboundEventId)
    {
        var normalizedProvider = provider.Trim();
        var normalizedEventId = providerInboundEventId.Trim();
        return db.ReplyEvents
            .AsNoTracking()
            .FirstOrDefault(x => x.Provider == normalizedProvider && x.ProviderInboundEventId == normalizedEventId) is { } entity
                ? ToDomain(entity)
                : null;
    }

    public ReplySummary GetSummary(Guid mailingId)
    {
        var last = db.ReplyEvents
            .AsNoTracking()
            .Where(x => x.MailingId == mailingId)
            .OrderByDescending(x => x.ReceivedAt)
            .FirstOrDefault();
        var count = db.ReplyEvents.Count(x => x.MailingId == mailingId);
        return new ReplySummary(
            mailingId,
            count,
            last?.ReceivedAt,
            last is null ? null : Enum.TryParse<ReplyProcessingStatus>(last.ProcessingStatus, out var status) ? status : ReplyProcessingStatus.Received);
    }

    public int CountByMailing(Guid mailingId) => db.ReplyEvents.Count(x => x.MailingId == mailingId);

    public ReplyEvent? GetLastByMailing(Guid mailingId) => db.ReplyEvents
        .AsNoTracking()
        .Where(x => x.MailingId == mailingId)
        .OrderByDescending(x => x.ReceivedAt)
        .Select(x => ToDomain(x))
        .FirstOrDefault();

    public IReadOnlyCollection<ReplyEvent> ListRecentByMailing(Guid mailingId, int limit) => db.ReplyEvents
        .AsNoTracking()
        .Where(x => x.MailingId == mailingId)
        .OrderByDescending(x => x.ReceivedAt)
        .Take(Math.Max(1, limit))
        .Select(x => ToDomain(x))
        .ToArray();

    public IReadOnlyCollection<ReplyEvent> ListRecent(int limit) => db.ReplyEvents
        .AsNoTracking()
        .OrderByDescending(x => x.ReceivedAt)
        .Take(Math.Max(1, limit))
        .Select(x => ToDomain(x))
        .ToArray();

    public IReadOnlyCollection<ReplyEvent> FindPendingForward(DateTimeOffset now, int batchSize) => db.ReplyEvents
        .AsNoTracking()
        .Where(x =>
            x.ProcessingStatus == ReplyProcessingStatus.QueuedForForward.ToString() ||
            (x.ProcessingStatus == ReplyProcessingStatus.Failed.ToString() && x.ForwardRetryCount < 3))
        .OrderBy(x => x.ForwardQueuedAt ?? x.ReceivedAt)
        .Take(Math.Max(1, batchSize))
        .Select(x => ToDomain(x))
        .ToArray();

    public IReadOnlyCollection<ReplyEvent> FindExpiredBodies(DateTimeOffset now, int batchSize) => db.ReplyEvents
        .AsNoTracking()
        .Where(x => x.BodyStorageStatus == ReplyBodyStorageStatus.StoredTemporarily.ToString() && x.BodyExpiresAt != null && x.BodyExpiresAt <= now)
        .OrderBy(x => x.BodyExpiresAt)
        .Take(Math.Max(1, batchSize))
        .Select(x => ToDomain(x))
        .ToArray();

    public void Save(ReplyEvent replyEvent)
    {
        var entity = db.ReplyEvents.FirstOrDefault(x => x.Id == replyEvent.Id);
        if (entity is null)
        {
            db.ReplyEvents.Add(ToEntity(replyEvent));
        }
        else
        {
            UpdateEntity(entity, replyEvent);
        }

        db.SaveChanges();
    }

    public void MarkForwardQueued(Guid replyEventId)
    {
        if (db.ReplyEvents.FirstOrDefault(x => x.Id == replyEventId) is { } entity)
        {
            entity.ProcessingStatus = ReplyProcessingStatus.QueuedForForward.ToString();
            entity.ForwardQueuedAt ??= DateTimeOffset.UtcNow;
            entity.ProcessedAt ??= DateTimeOffset.UtcNow;
            db.SaveChanges();
        }
    }

    public void MarkForwarded(Guid replyEventId)
    {
        if (db.ReplyEvents.FirstOrDefault(x => x.Id == replyEventId) is { } entity)
        {
            entity.ProcessingStatus = ReplyProcessingStatus.Forwarded.ToString();
            entity.ForwardedAt = DateTimeOffset.UtcNow;
            entity.ErrorCode = null;
            entity.ErrorMessage = null;
            db.SaveChanges();
        }
    }

    public void MarkForwardFailed(Guid replyEventId, string errorCode, string errorMessage)
    {
        if (db.ReplyEvents.FirstOrDefault(x => x.Id == replyEventId) is { } entity)
        {
            entity.ProcessingStatus = ReplyProcessingStatus.Failed.ToString();
            entity.ForwardRetryCount++;
            entity.ErrorCode = errorCode;
            entity.ErrorMessage = errorMessage;
            db.SaveChanges();
        }
    }

    public void MarkBodyDeleted(Guid replyEventId)
    {
        if (db.ReplyEvents.FirstOrDefault(x => x.Id == replyEventId) is { } entity)
        {
            entity.BodyStorageStatus = ReplyBodyStorageStatus.Deleted.ToString();
            entity.BodyTextStored = null;
            entity.BodyExpiresAt = null;
            db.SaveChanges();
        }
    }

    private static ReplyEventEntity ToEntity(ReplyEvent item)
    {
        var entity = new ReplyEventEntity { Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id };
        UpdateEntity(entity, item);
        return entity;
    }

    private static void UpdateEntity(ReplyEventEntity entity, ReplyEvent item)
    {
        entity.Provider = item.Provider ?? string.Empty;
        entity.ProviderInboundEventId = item.ProviderInboundEventId ?? string.Empty;
        entity.MailingId = item.MailingId;
        entity.ClientId = item.ClientId;
        entity.RecipientEmailNormalized = item.RecipientEmailNormalized ?? string.Empty;
        entity.FromEmailNormalized = item.FromEmailNormalized ?? string.Empty;
        entity.ToAddress = item.ToAddress ?? string.Empty;
        entity.ReplyTokenHash = item.ReplyTokenHash;
        entity.SubjectPreview = item.SubjectPreview ?? string.Empty;
        entity.ReceivedAt = item.ReceivedAt.ToUniversalTime();
        entity.ProcessedAt = item.ProcessedAt?.ToUniversalTime();
        entity.ForwardQueuedAt = item.ForwardQueuedAt?.ToUniversalTime();
        entity.ForwardedAt = item.ForwardedAt?.ToUniversalTime();
        entity.ForwardToEmailNormalized = item.ForwardToEmailNormalized;
        entity.ProcessingStatus = item.ProcessingStatus.ToString();
        entity.ForwardRetryCount = item.ForwardRetryCount;
        entity.BodyStorageStatus = item.BodyStorageStatus.ToString();
        entity.BodyExpiresAt = item.BodyExpiresAt?.ToUniversalTime();
        entity.BodyTextStored = item.BodyTextStored;
        entity.RawPayloadHash = item.RawPayloadHash ?? string.Empty;
        entity.ErrorCode = item.ErrorCode;
        entity.ErrorMessage = item.ErrorMessage;
    }

    private static ReplyEvent ToDomain(ReplyEventEntity entity) => new(
        entity.Id,
        entity.Provider,
        entity.ProviderInboundEventId,
        entity.MailingId,
        entity.ClientId,
        entity.RecipientEmailNormalized,
        entity.FromEmailNormalized,
        entity.ToAddress,
        entity.ReplyTokenHash,
        entity.SubjectPreview,
        entity.ReceivedAt,
        entity.ProcessedAt,
        entity.ForwardQueuedAt,
        entity.ForwardedAt,
        entity.ForwardToEmailNormalized,
        Enum.TryParse<ReplyProcessingStatus>(entity.ProcessingStatus, out var processingStatus) ? processingStatus : ReplyProcessingStatus.Received,
        entity.ForwardRetryCount,
        Enum.TryParse<ReplyBodyStorageStatus>(entity.BodyStorageStatus, out var bodyStatus) ? bodyStatus : ReplyBodyStorageStatus.NotStored,
        entity.BodyExpiresAt,
        entity.BodyTextStored,
        entity.RawPayloadHash,
        entity.ErrorCode,
        entity.ErrorMessage);
}

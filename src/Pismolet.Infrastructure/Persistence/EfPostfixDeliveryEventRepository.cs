using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfPostfixDeliveryEventRepository(PismoletDbContext db) : IPostfixDeliveryEventRepository
{
    public PostfixDeliveryEvent AddIfNotExists(PostfixDeliveryEvent deliveryEvent)
    {
        var queueId = PostfixDeliveryEvent.NormalizeQueueId(deliveryEvent.QueueId);
        var recipientEmail = PostfixDeliveryEvent.NormalizeEmail(deliveryEvent.RecipientEmail);
        var status = deliveryEvent.Status.ToString();
        var occurredAt = deliveryEvent.OccurredAt.ToUniversalTime();
        var existing = db.Set<PostfixDeliveryEventEntity>()
            .AsNoTracking()
            .FirstOrDefault(x => x.QueueId == queueId && x.RecipientEmail == recipientEmail && x.Status == status && x.OccurredAt == occurredAt);
        if (existing is not null)
        {
            return ToDomain(existing);
        }

        var entity = ToEntity(deliveryEvent with { QueueId = queueId, RecipientEmail = recipientEmail, OccurredAt = occurredAt });
        db.Set<PostfixDeliveryEventEntity>().Add(entity);
        db.SaveChanges();
        return ToDomain(entity);
    }

    public PostfixDeliveryEvent? GetByQueueIdAndRecipient(string queueId, string recipientEmail)
    {
        var normalizedQueueId = PostfixDeliveryEvent.NormalizeQueueId(queueId);
        var normalizedRecipient = PostfixDeliveryEvent.NormalizeEmail(recipientEmail);
        return db.Set<PostfixDeliveryEventEntity>()
            .AsNoTracking()
            .Where(x => x.QueueId == normalizedQueueId && x.RecipientEmail == normalizedRecipient)
            .OrderByDescending(x => x.OccurredAt)
            .FirstOrDefault() is { } entity
                ? ToDomain(entity)
                : null;
    }

    public IReadOnlyCollection<PostfixDeliveryEvent> ListRecent(int limit) => db.Set<PostfixDeliveryEventEntity>()
        .AsNoTracking()
        .OrderByDescending(x => x.OccurredAt)
        .Take(Math.Max(1, limit))
        .Select(x => ToDomain(x))
        .ToArray();

    public IReadOnlyCollection<PostfixDeliveryEvent> ListByRecipient(string recipientEmail, int limit)
    {
        var normalized = PostfixDeliveryEvent.NormalizeEmail(recipientEmail);
        return db.Set<PostfixDeliveryEventEntity>()
            .AsNoTracking()
            .Where(x => x.RecipientEmail == normalized)
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Max(1, limit))
            .Select(x => ToDomain(x))
            .ToArray();
    }

    private static PostfixDeliveryEventEntity ToEntity(PostfixDeliveryEvent item) => new()
    {
        Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
        QueueId = PostfixDeliveryEvent.NormalizeQueueId(item.QueueId),
        RecipientEmail = PostfixDeliveryEvent.NormalizeEmail(item.RecipientEmail),
        Status = item.Status.ToString(),
        DeliveryStatus = item.DeliveryStatus.ToString(),
        Dsn = PostfixDeliveryEvent.NormalizeNullable(item.Dsn),
        Relay = PostfixDeliveryEvent.NormalizeNullable(item.Relay),
        Diagnostic = PostfixDeliveryEvent.NormalizeNullable(item.Diagnostic),
        OccurredAt = item.OccurredAt.ToUniversalTime(),
        CreatedAt = item.CreatedAt.ToUniversalTime()
    };

    private static PostfixDeliveryEvent ToDomain(PostfixDeliveryEventEntity item) => new(
        item.Id,
        item.QueueId,
        item.RecipientEmail,
        Enum.TryParse<PostfixDeliveryEventStatus>(item.Status, out var status) ? status : PostfixDeliveryEventStatus.Unknown,
        Enum.TryParse<DeliveryStatus>(item.DeliveryStatus, out var deliveryStatus) ? deliveryStatus : DeliveryStatus.Unknown,
        item.Dsn,
        item.Relay,
        item.Diagnostic,
        item.OccurredAt,
        item.CreatedAt);
}

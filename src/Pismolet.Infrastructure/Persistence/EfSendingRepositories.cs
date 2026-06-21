using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfSendEventRepository(PismoletDbContext db) : ISendEventRepository
{
    public SendEvent? Get(Guid mailingId, string recipientEmail)
    {
        var normalized = Normalize(recipientEmail);
        return db.SendEvents.AsNoTracking().FirstOrDefault(x => x.MailingId == mailingId && x.RecipientEmail == normalized) is { } entity ? ToDomain(entity) : null;
    }

    public SendEvent? GetByProviderMessageId(string providerMessageId)
    {
        var normalized = providerMessageId.Trim();
        return db.SendEvents.AsNoTracking().FirstOrDefault(x => x.ProviderMessageId == normalized) is { } entity ? ToDomain(entity) : null;
    }

    public IReadOnlyCollection<SendEvent> ListByMailingId(Guid mailingId) => db.SendEvents.AsNoTracking().Where(x => x.MailingId == mailingId).OrderBy(x => x.CreatedAt).ThenBy(x => x.RecipientEmail).Select(x => ToDomain(x)).ToArray();

    public IReadOnlyCollection<SendEvent> GetPendingBatch(Guid mailingId, int batchSize) => db.SendEvents.AsNoTracking().Where(x => x.MailingId == mailingId && x.Status == SendEventStatus.Pending.ToString()).OrderBy(x => x.CreatedAt).ThenBy(x => x.RecipientEmail).Take(Math.Max(1, batchSize)).Select(x => ToDomain(x)).ToArray();

    public int CountAcceptedForOwnerOnUtcDate(string ownerEmail, DateOnly utcDate)
    {
        var normalized = Normalize(ownerEmail);
        var targetDate = utcDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Date;

        return db.SendEvents
            .AsNoTracking()
            .Where(x =>
                x.Status == SendEventStatus.Accepted.ToString() &&
                x.OwnerEmail == normalized &&
                x.AcceptedAt != null)
            .Select(x => x.AcceptedAt!.Value)
            .AsEnumerable()
            .Count(acceptedAt => acceptedAt.UtcDateTime.Date == targetDate);
    }

    public void Save(SendEvent sendEvent)
    {
        var recipientEmail = Normalize(sendEvent.RecipientEmail);
        var entity = db.SendEvents.FirstOrDefault(x => x.MailingId == sendEvent.MailingId && x.RecipientEmail == recipientEmail);
        if (entity is null)
        {
            db.SendEvents.Add(ToEntity(sendEvent));
        }
        else
        {
            entity.OwnerEmail = Normalize(sendEvent.OwnerEmail);
            entity.Status = sendEvent.Status.ToString();
            entity.Reason = sendEvent.Reason.ToString();
            entity.Provider = sendEvent.Provider;
            entity.ProviderMessageId = sendEvent.ProviderMessageId;
            entity.Attempt = sendEvent.Attempt;
            entity.ErrorCode = sendEvent.ErrorCode;
            entity.ErrorMessage = sendEvent.ErrorMessage;
            entity.DeliveryStatus = sendEvent.DeliveryStatus.ToString();
            entity.LastDeliveryEventAt = sendEvent.LastDeliveryEventAt?.ToUniversalTime();
            entity.LastDeliverySummary = sendEvent.LastDeliverySummary;
            entity.CreatedAt = sendEvent.CreatedAt.ToUniversalTime();
            entity.UpdatedAt = sendEvent.UpdatedAt.ToUniversalTime();
            entity.AcceptedAt = sendEvent.AcceptedAt?.ToUniversalTime();
        }

        db.SaveChanges();
    }

    public MailingSendSummary GetSummary(Guid mailingId, int totalAcceptedRecipients)
    {
        var events = ListByMailingId(mailingId);
        var suppressed = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason == SendSkipReason.GlobalSuppression);
        var clientSuppressed = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason == SendSkipReason.ClientSuppression);
        var pausedByLimit = events.Count(x => x.Status == SendEventStatus.Paused && x.Reason == SendSkipReason.DailyLimit);
        var skippedOther = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason is not SendSkipReason.GlobalSuppression and not SendSkipReason.ClientSuppression);
        return new MailingSendSummary(mailingId, events.Count(x => x.Status is SendEventStatus.Pending or SendEventStatus.Accepted or SendEventStatus.Failed), events.Count(x => x.Status == SendEventStatus.Accepted), events.Count(x => x.Status == SendEventStatus.Failed), suppressed, clientSuppressed, pausedByLimit, skippedOther, events.Count(x => x.Status == SendEventStatus.Pending), totalAcceptedRecipients, events.Count(x => x.DeliveryStatus == DeliveryStatus.Accepted), events.Count(x => x.DeliveryStatus == DeliveryStatus.Delivered), events.Count(x => x.DeliveryStatus == DeliveryStatus.SoftBounce), events.Count(x => x.DeliveryStatus == DeliveryStatus.HardBounce), events.Count(x => x.DeliveryStatus == DeliveryStatus.Complaint), events.Count(x => x.DeliveryStatus == DeliveryStatus.Rejected), events.Count(x => x.DeliveryStatus == DeliveryStatus.Unknown));
    }

    private static SendEventEntity ToEntity(SendEvent sendEvent) => new()
    {
        Id = sendEvent.Id == Guid.Empty ? Guid.NewGuid() : sendEvent.Id,
        MailingId = sendEvent.MailingId,
        OwnerEmail = Normalize(sendEvent.OwnerEmail),
        RecipientEmail = Normalize(sendEvent.RecipientEmail),
        Status = sendEvent.Status.ToString(),
        Reason = sendEvent.Reason.ToString(),
        Provider = sendEvent.Provider,
        ProviderMessageId = sendEvent.ProviderMessageId,
        Attempt = sendEvent.Attempt,
        ErrorCode = sendEvent.ErrorCode,
        ErrorMessage = sendEvent.ErrorMessage,
        DeliveryStatus = sendEvent.DeliveryStatus.ToString(),
        LastDeliveryEventAt = sendEvent.LastDeliveryEventAt?.ToUniversalTime(),
        LastDeliverySummary = sendEvent.LastDeliverySummary,
        CreatedAt = sendEvent.CreatedAt.ToUniversalTime(),
        UpdatedAt = sendEvent.UpdatedAt.ToUniversalTime(),
        AcceptedAt = sendEvent.AcceptedAt?.ToUniversalTime()
    };

    private static SendEvent ToDomain(SendEventEntity entity) => new(entity.Id, entity.MailingId, entity.OwnerEmail, entity.RecipientEmail, Enum.Parse<SendEventStatus>(entity.Status), Enum.Parse<SendSkipReason>(entity.Reason), entity.Provider, entity.ProviderMessageId, entity.Attempt, entity.ErrorCode, entity.ErrorMessage, entity.CreatedAt, entity.UpdatedAt, Enum.TryParse<DeliveryStatus>(entity.DeliveryStatus, out var status) ? status : DeliveryStatus.NotReported, entity.LastDeliveryEventAt, entity.LastDeliverySummary, entity.AcceptedAt);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

public sealed class EfProviderWebhookEventRepository(PismoletDbContext db) : IProviderWebhookEventRepository
{
    public ProviderWebhookEvent? GetByProviderEventId(string provider, string providerEventId)
    {
        var normalizedProvider = provider.Trim();
        var normalizedEventId = providerEventId.Trim();
        return db.ProviderWebhookEvents.AsNoTracking().FirstOrDefault(x => x.Provider == normalizedProvider && x.ProviderEventId == normalizedEventId) is { } entity ? ToDomain(entity) : null;
    }

    public IReadOnlyCollection<ProviderWebhookEvent> ListByMailingId(Guid mailingId) => db.ProviderWebhookEvents.AsNoTracking().Where(x => x.MailingId == mailingId).OrderBy(x => x.ReceivedAt).Select(x => ToDomain(x)).ToArray();

    public IReadOnlyCollection<ProviderWebhookEvent> ListRecent(int limit) => db.ProviderWebhookEvents.AsNoTracking().OrderByDescending(x => x.ReceivedAt).Take(Math.Max(1, limit)).Select(x => ToDomain(x)).ToArray();

    public void Save(ProviderWebhookEvent webhookEvent)
    {
        var existing = db.ProviderWebhookEvents.FirstOrDefault(x => x.Provider == webhookEvent.Provider && x.ProviderEventId == webhookEvent.ProviderEventId);
        if (existing is null)
        {
            db.ProviderWebhookEvents.Add(ToEntity(webhookEvent));
        }
        else
        {
            existing.ProcessingStatus = webhookEvent.ProcessingStatus.ToString();
            existing.ReasonCode = webhookEvent.ReasonCode;
            existing.ReasonMessage = webhookEvent.ReasonMessage;
        }

        db.SaveChanges();
    }

    private static ProviderWebhookEventEntity ToEntity(ProviderWebhookEvent item) => new()
    {
        Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
        Provider = item.Provider,
        ProviderEventId = item.ProviderEventId,
        ProviderMessageId = item.ProviderMessageId,
        MailingId = item.MailingId,
        ClientId = item.ClientId,
        RecipientEmailNormalized = item.RecipientEmailNormalized,
        EventType = item.EventType.ToString(),
        OccurredAt = item.OccurredAt.ToUniversalTime(),
        ReceivedAt = item.ReceivedAt.ToUniversalTime(),
        RawPayloadHash = item.RawPayloadHash,
        RawPayloadStored = item.RawPayloadStored,
        ReasonCode = item.ReasonCode,
        ReasonMessage = item.ReasonMessage,
        ProcessingStatus = item.ProcessingStatus.ToString(),
        CorrelationId = item.CorrelationId
    };

    private static ProviderWebhookEvent ToDomain(ProviderWebhookEventEntity item) => new(
        item.Id,
        item.Provider,
        item.ProviderEventId,
        item.ProviderMessageId,
        item.MailingId,
        item.ClientId,
        item.RecipientEmailNormalized,
        Enum.TryParse<ProviderWebhookEventType>(item.EventType, out var type) ? type : ProviderWebhookEventType.Unknown,
        item.OccurredAt,
        item.ReceivedAt,
        item.RawPayloadHash,
        item.RawPayloadStored,
        item.ReasonCode,
        item.ReasonMessage,
        Enum.TryParse<ProviderWebhookProcessingStatus>(item.ProcessingStatus, out var status) ? status : ProviderWebhookProcessingStatus.Failed,
        item.CorrelationId);
}

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
        var start = new DateTimeOffset(utcDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = start.AddDays(1);
        return db.SendEvents.Count(x =>
            x.Status == SendEventStatus.Accepted.ToString() &&
            x.OwnerEmail == normalized &&
            x.AcceptedAt != null &&
            x.AcceptedAt >= start &&
            x.AcceptedAt < end);
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

    private static ProviderWebhookEvent ToDomain(ProviderWebhookEventEntity entity) => new(entity.Id, entity.Provider, entity.ProviderEventId, entity.ProviderMessageId, entity.MailingId, entity.ClientId, entity.RecipientEmailNormalized, Enum.Parse<ProviderWebhookEventType>(entity.EventType), entity.OccurredAt, entity.ReceivedAt, entity.RawPayloadHash, entity.RawPayloadStored, entity.ReasonCode, entity.ReasonMessage, Enum.Parse<ProviderWebhookProcessingStatus>(entity.ProcessingStatus), entity.CorrelationId);
}

public sealed class EfClientSuppressionRepository(PismoletDbContext db) : IClientSuppressionRepository
{
    public bool IsSuppressed(string clientId, string normalizedEmail) => db.ClientSuppressions.Any(x => x.ClientId == Normalize(clientId) && x.EmailNormalized == Normalize(normalizedEmail));

    public IReadOnlySet<string> GetSuppressedSet(string clientId, IEnumerable<string> normalizedEmails)
    {
        var normalizedClient = Normalize(clientId);
        var emails = normalizedEmails.Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (emails.Length == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return db.ClientSuppressions.AsNoTracking().Where(x => x.ClientId == normalizedClient && emails.Contains(x.EmailNormalized)).Select(x => x.EmailNormalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ClientSuppression> ListRecent(int limit) => db.ClientSuppressions.AsNoTracking().OrderByDescending(x => x.LastSeenAt).Take(Math.Max(1, limit)).Select(x => ToDomain(x)).ToArray();

    public ClientSuppression AddOrUpdate(ClientSuppression suppression)
    {
        var clientId = Normalize(suppression.ClientId);
        var email = Normalize(suppression.EmailNormalized);
        var entity = db.ClientSuppressions.FirstOrDefault(x => x.ClientId == clientId && x.EmailNormalized == email);
        if (entity is null)
        {
            entity = ToEntity(suppression with { ClientId = clientId, EmailNormalized = email });
            db.ClientSuppressions.Add(entity);
        }
        else
        {
            entity.LastSeenAt = DateTimeOffset.UtcNow;
            entity.SourceMailingId ??= suppression.SourceMailingId;
            entity.SourceProviderMessageId ??= suppression.SourceProviderMessageId;
        }
        db.SaveChanges();
        return ToDomain(entity);
    }

    private static ClientSuppressionEntity ToEntity(ClientSuppression x) => new() { Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id, ClientId = Normalize(x.ClientId), EmailNormalized = Normalize(x.EmailNormalized), Reason = x.Reason.ToString(), SourceMailingId = x.SourceMailingId, SourceProviderMessageId = x.SourceProviderMessageId, CreatedAt = x.CreatedAt.ToUniversalTime(), LastSeenAt = x.LastSeenAt.ToUniversalTime() };
    private static ClientSuppression ToDomain(ClientSuppressionEntity x) => new(x.Id, x.ClientId, x.EmailNormalized, Enum.Parse<ClientSuppressionReason>(x.Reason), x.SourceMailingId, x.SourceProviderMessageId, x.CreatedAt, x.LastSeenAt);
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
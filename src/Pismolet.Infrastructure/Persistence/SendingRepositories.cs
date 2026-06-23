using System.Collections.Concurrent;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemorySendEventRepository : ISendEventRepository
{
    private readonly ConcurrentDictionary<string, SendEvent> _items = new(StringComparer.OrdinalIgnoreCase);

    public SendEvent? Get(Guid mailingId, string recipientEmail) => _items.GetValueOrDefault(Key(mailingId, recipientEmail));

    public SendEvent? GetByProviderMessageId(string providerMessageId) => _items.Values.FirstOrDefault(x =>
        !string.IsNullOrWhiteSpace(x.ProviderMessageId) &&
        string.Equals(x.ProviderMessageId.Trim(), providerMessageId.Trim(), StringComparison.OrdinalIgnoreCase));

    public SendEvent? GetByTrackingToken(string trackingToken) => _items.Values.FirstOrDefault(x =>
        !string.IsNullOrWhiteSpace(x.TrackingToken) &&
        string.Equals(x.TrackingToken.Trim(), trackingToken.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<SendEvent> ListByMailingId(Guid mailingId) => _items.Values
        .Where(x => x.MailingId == mailingId)
        .OrderBy(x => x.CreatedAt)
        .ThenBy(x => x.RecipientEmail, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyCollection<SendEvent> GetPendingBatch(Guid mailingId, int batchSize) => _items.Values
        .Where(x => x.MailingId == mailingId && x.Status == SendEventStatus.Pending)
        .OrderBy(x => x.CreatedAt)
        .ThenBy(x => x.RecipientEmail, StringComparer.OrdinalIgnoreCase)
        .Take(Math.Max(1, batchSize))
        .ToArray();

    public int CountAcceptedForOwnerOnUtcDate(string ownerEmail, DateOnly utcDate) => _items.Values.Count(x =>
        x.Status == SendEventStatus.Accepted &&
        x.AcceptedAt is not null &&
        string.Equals(x.OwnerEmail, ownerEmail.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) &&
        DateOnly.FromDateTime(x.AcceptedAt.Value.UtcDateTime) == utcDate);

    public IReadOnlyCollection<MailWarmupAcceptedSend> ListAcceptedForWarmupWindow(string ownerEmail, DateTimeOffset sinceUtc)
    {
        var normalized = ownerEmail.Trim().ToLowerInvariant();
        var since = sinceUtc.ToUniversalTime();

        return _items.Values
            .Where(x =>
                x.Status == SendEventStatus.Accepted &&
                x.AcceptedAt is not null &&
                string.Equals(x.OwnerEmail, normalized, StringComparison.OrdinalIgnoreCase) &&
                x.AcceptedAt.Value.ToUniversalTime() >= since)
            .OrderBy(x => x.AcceptedAt!.Value)
            .ThenBy(x => x.RecipientEmail, StringComparer.OrdinalIgnoreCase)
            .Select(x => new MailWarmupAcceptedSend(x.RecipientEmail, x.AcceptedAt!.Value.ToUniversalTime()))
            .ToArray();
    }

    public IReadOnlyCollection<SoftBounceDeliveryStats> ListSoftBounceStats(string ownerEmail, IEnumerable<string> normalizedEmails)
    {
        var normalizedOwner = ownerEmail.Trim().ToLowerInvariant();
        var emails = normalizedEmails
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (emails.Count == 0)
        {
            return Array.Empty<SoftBounceDeliveryStats>();
        }

        return _items.Values
            .Where(x =>
                string.Equals(x.OwnerEmail, normalizedOwner, StringComparison.OrdinalIgnoreCase) &&
                x.DeliveryStatus == DeliveryStatus.SoftBounce &&
                emails.Contains(x.RecipientEmail))
            .GroupBy(x => x.RecipientEmail, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SoftBounceDeliveryStats(
                group.Key,
                group.Count(),
                group.Where(x => x.LastDeliveryEventAt is not null).Select(x => x.LastDeliveryEventAt).OrderByDescending(x => x).FirstOrDefault(),
                group.OrderByDescending(x => x.LastDeliveryEventAt ?? x.UpdatedAt).Select(x => x.LastDeliverySummary).FirstOrDefault()))
            .ToArray();
    }

    public void Save(SendEvent sendEvent) => _items[Key(sendEvent.MailingId, sendEvent.RecipientEmail)] = sendEvent;

    public MailingSendSummary GetSummary(Guid mailingId, int totalAcceptedRecipients)
    {
        var events = ListByMailingId(mailingId);
        var suppressed = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason == SendSkipReason.GlobalSuppression);
        var clientSuppressed = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason == SendSkipReason.ClientSuppression);
        var pausedByLimit = events.Count(x => x.Status == SendEventStatus.Paused && x.Reason is SendSkipReason.DailyLimit or SendSkipReason.WarmupLimit);
        var skippedOther = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason is not SendSkipReason.GlobalSuppression and not SendSkipReason.ClientSuppression);
        return new MailingSendSummary(
            mailingId,
            events.Count(x => x.Status is SendEventStatus.Pending or SendEventStatus.Accepted or SendEventStatus.Failed),
            events.Count(x => x.Status == SendEventStatus.Accepted),
            events.Count(x => x.Status == SendEventStatus.Failed),
            suppressed,
            clientSuppressed,
            pausedByLimit,
            skippedOther,
            events.Count(x => x.Status == SendEventStatus.Pending),
            totalAcceptedRecipients,
            events.Count(x => x.DeliveryStatus == DeliveryStatus.Accepted),
            events.Count(x => x.DeliveryStatus == DeliveryStatus.Delivered),
            events.Count(x => x.DeliveryStatus == DeliveryStatus.SoftBounce),
            events.Count(x => x.DeliveryStatus == DeliveryStatus.HardBounce),
            events.Count(x => x.DeliveryStatus == DeliveryStatus.Complaint),
            events.Count(x => x.DeliveryStatus == DeliveryStatus.Rejected),
            events.Count(x => x.DeliveryStatus == DeliveryStatus.Unknown));
    }

    private static string Key(Guid mailingId, string recipientEmail) => $"{mailingId:N}:{recipientEmail.Trim().ToLowerInvariant()}";
}

public sealed class InMemoryProviderWebhookEventRepository : IProviderWebhookEventRepository
{
    private readonly ConcurrentDictionary<string, ProviderWebhookEvent> _items = new(StringComparer.OrdinalIgnoreCase);

    public ProviderWebhookEvent? GetByProviderEventId(string provider, string providerEventId) => _items.GetValueOrDefault(Key(provider, providerEventId));

    public IReadOnlyCollection<ProviderWebhookEvent> ListByMailingId(Guid mailingId) => _items.Values
        .Where(x => x.MailingId == mailingId)
        .OrderBy(x => x.ReceivedAt)
        .ToArray();

    public IReadOnlyCollection<ProviderWebhookEvent> ListRecent(int limit) => _items.Values
        .OrderByDescending(x => x.ReceivedAt)
        .Take(Math.Max(1, limit))
        .ToArray();

    public void Save(ProviderWebhookEvent webhookEvent) => _items[Key(webhookEvent.Provider, webhookEvent.ProviderEventId)] = webhookEvent;

    private static string Key(string provider, string providerEventId) => $"{provider.Trim().ToLowerInvariant()}:{providerEventId.Trim().ToLowerInvariant()}";
}

public sealed class InMemoryClientSuppressionRepository : IClientSuppressionRepository
{
    private readonly ConcurrentDictionary<string, ClientSuppression> _items = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSuppressed(string clientId, string normalizedEmail) => _items.ContainsKey(Key(clientId, normalizedEmail));

    public IReadOnlySet<string> GetSuppressedSet(string clientId, IEnumerable<string> normalizedEmails)
    {
        var normalizedClient = Normalize(clientId);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in normalizedEmails.Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_items.ContainsKey(Key(normalizedClient, email)))
            {
                result.Add(email);
            }
        }

        return result;
    }

    public IReadOnlyCollection<ClientSuppression> ListRecent(int limit) => _items.Values
        .OrderByDescending(x => x.CreatedAt)
        .Take(Math.Max(1, limit))
        .ToArray();

    public ClientSuppression AddOrUpdate(ClientSuppression suppression)
    {
        var item = suppression with
        {
            ClientId = Normalize(suppression.ClientId),
            EmailNormalized = Normalize(suppression.EmailNormalized)
        };

        return _items.AddOrUpdate(
            Key(item.ClientId, item.EmailNormalized),
            item,
            (_, existing) => existing.Touch(item.SourceMailingId, item.SourceProviderMessageId));
    }

    private static string Key(string clientId, string email) => $"{Normalize(clientId)}:{Normalize(email)}";

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed class InMemoryReplyEventRepository : IReplyEventRepository
{
    private readonly ConcurrentDictionary<string, ReplyEvent> _byProviderEvent = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, ReplyEvent> _byId = new();

    public ReplyEvent AddIfNotExists(ReplyEvent replyEvent)
    {
        var key = Key(replyEvent.Provider, replyEvent.ProviderInboundEventId);
        var item = _byProviderEvent.GetOrAdd(key, replyEvent);
        _byId[item.Id] = item;
        return item;
    }

    public ReplyEvent? Get(Guid id) => _byId.GetValueOrDefault(id);

    public ReplyEvent? GetByProviderEventId(string provider, string providerInboundEventId) => _byProviderEvent.GetValueOrDefault(Key(provider, providerInboundEventId));

    public ReplySummary GetSummary(Guid mailingId)
    {
        var items = _byId.Values.Where(x => x.MailingId == mailingId).OrderByDescending(x => x.ReceivedAt).ToArray();
        var last = items.FirstOrDefault();
        return new ReplySummary(mailingId, items.Length, last?.ReceivedAt, last?.ProcessingStatus);
    }

    public int CountByMailing(Guid mailingId) => _byId.Values.Count(x => x.MailingId == mailingId);

    public ReplyEvent? GetLastByMailing(Guid mailingId) => _byId.Values
        .Where(x => x.MailingId == mailingId)
        .OrderByDescending(x => x.ReceivedAt)
        .FirstOrDefault();

    public IReadOnlyCollection<ReplyEvent> ListRecentByMailing(Guid mailingId, int limit) => _byId.Values
        .Where(x => x.MailingId == mailingId)
        .OrderByDescending(x => x.ReceivedAt)
        .Take(Math.Max(1, limit))
        .ToArray();

    public IReadOnlyCollection<ReplyEvent> ListRecent(int limit) => _byId.Values
        .OrderByDescending(x => x.ReceivedAt)
        .Take(Math.Max(1, limit))
        .ToArray();

    public IReadOnlyCollection<ReplyEvent> FindPendingForward(DateTimeOffset now, int batchSize) => _byId.Values
        .Where(x => x.ProcessingStatus == ReplyProcessingStatus.QueuedForForward ||
            (x.ProcessingStatus == ReplyProcessingStatus.Failed && x.ForwardRetryCount < 3))
        .OrderBy(x => x.ForwardQueuedAt ?? x.ReceivedAt)
        .Take(Math.Max(1, batchSize))
        .ToArray();

    public IReadOnlyCollection<ReplyEvent> FindExpiredBodies(DateTimeOffset now, int batchSize) => _byId.Values
        .Where(x => x.BodyStorageStatus == ReplyBodyStorageStatus.StoredTemporarily && x.BodyExpiresAt is not null && x.BodyExpiresAt <= now)
        .OrderBy(x => x.BodyExpiresAt)
        .Take(Math.Max(1, batchSize))
        .ToArray();

    public void Save(ReplyEvent replyEvent)
    {
        _byId[replyEvent.Id] = replyEvent;
        _byProviderEvent[Key(replyEvent.Provider, replyEvent.ProviderInboundEventId)] = replyEvent;
    }

    public void MarkForwardQueued(Guid replyEventId)
    {
        if (Get(replyEventId) is { } item) Save(item.MarkQueuedForForward());
    }

    public void MarkForwarded(Guid replyEventId)
    {
        if (Get(replyEventId) is { } item) Save(item.MarkForwarded());
    }

    public void MarkForwardFailed(Guid replyEventId, string errorCode, string errorMessage)
    {
        if (Get(replyEventId) is { } item) Save(item.MarkForwardFailed(errorCode, errorMessage));
    }

    public void MarkBodyDeleted(Guid replyEventId)
    {
        if (Get(replyEventId) is { } item) Save(item.MarkBodyDeleted());
    }

    private static string Key(string provider, string providerInboundEventId) => $"{provider.Trim().ToLowerInvariant()}:{providerInboundEventId.Trim().ToLowerInvariant()}";
}

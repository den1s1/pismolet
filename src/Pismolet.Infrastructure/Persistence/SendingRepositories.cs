using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemorySendEventRepository : ISendEventRepository
{
    private readonly ConcurrentDictionary<string, SendEvent> _items = new(StringComparer.OrdinalIgnoreCase);

    public SendEvent? Get(Guid mailingId, string recipientEmail) => _items.GetValueOrDefault(Key(mailingId, recipientEmail));

    public SendEvent? GetByProviderMessageId(string providerMessageId) => _items.Values.FirstOrDefault(x =>
        !string.IsNullOrWhiteSpace(x.ProviderMessageId) &&
        string.Equals(x.ProviderMessageId, providerMessageId.Trim(), StringComparison.OrdinalIgnoreCase));

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
        string.Equals(x.OwnerEmail, ownerEmail.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) &&
        DateOnly.FromDateTime(x.UpdatedAt.UtcDateTime) == utcDate);

    public void Save(SendEvent sendEvent) => _items[Key(sendEvent.MailingId, sendEvent.RecipientEmail)] = sendEvent;

    public MailingSendSummary GetSummary(Guid mailingId, int totalAcceptedRecipients)
    {
        var events = ListByMailingId(mailingId);
        var suppressed = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason == SendSkipReason.GlobalSuppression);
        var clientSuppressed = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason == SendSkipReason.ClientSuppression);
        var pausedByLimit = events.Count(x => x.Status == SendEventStatus.Paused && x.Reason == SendSkipReason.DailyLimit);
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
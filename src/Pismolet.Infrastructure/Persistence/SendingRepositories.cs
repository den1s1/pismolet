using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemorySendEventRepository : ISendEventRepository
{
    private readonly ConcurrentDictionary<string, SendEvent> _items = new(StringComparer.OrdinalIgnoreCase);

    public SendEvent? Get(Guid mailingId, string recipientEmail) => _items.GetValueOrDefault(Key(mailingId, recipientEmail));

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
        var pausedByLimit = events.Count(x => x.Status == SendEventStatus.Paused && x.Reason == SendSkipReason.DailyLimit);
        var skippedOther = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason != SendSkipReason.GlobalSuppression);
        return new MailingSendSummary(
            mailingId,
            events.Count(x => x.Status is SendEventStatus.Pending or SendEventStatus.Accepted or SendEventStatus.Failed),
            events.Count(x => x.Status == SendEventStatus.Accepted),
            events.Count(x => x.Status == SendEventStatus.Failed),
            suppressed,
            pausedByLimit,
            skippedOther,
            events.Count(x => x.Status == SendEventStatus.Pending),
            totalAcceptedRecipients);
    }

    private static string Key(Guid mailingId, string recipientEmail) => $"{mailingId:N}:{recipientEmail.Trim().ToLowerInvariant()}";
}

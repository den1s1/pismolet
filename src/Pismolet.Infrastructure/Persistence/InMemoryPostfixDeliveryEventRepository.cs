using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryPostfixDeliveryEventRepository : IPostfixDeliveryEventRepository
{
    private readonly ConcurrentDictionary<string, PostfixDeliveryEvent> _items = new(StringComparer.OrdinalIgnoreCase);

    public PostfixDeliveryEvent AddIfNotExists(PostfixDeliveryEvent deliveryEvent)
    {
        var key = BuildKey(deliveryEvent.QueueId, deliveryEvent.RecipientEmail);
        var item = deliveryEvent with
        {
            QueueId = PostfixDeliveryEvent.NormalizeQueueId(deliveryEvent.QueueId),
            RecipientEmail = PostfixDeliveryEvent.NormalizeEmail(deliveryEvent.RecipientEmail)
        };
        return _items.GetOrAdd(key, item);
    }

    public PostfixDeliveryEvent? GetByQueueIdAndRecipient(string queueId, string recipientEmail) => _items.GetValueOrDefault(BuildKey(queueId, recipientEmail));

    public IReadOnlyCollection<PostfixDeliveryEvent> ListRecent(int limit) => _items.Values
        .OrderByDescending(x => x.OccurredAt)
        .Take(Math.Max(1, limit))
        .ToArray();

    public IReadOnlyCollection<PostfixDeliveryEvent> ListByRecipient(string recipientEmail, int limit)
    {
        var normalized = PostfixDeliveryEvent.NormalizeEmail(recipientEmail);
        return _items.Values
            .Where(x => string.Equals(x.RecipientEmail, normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static string BuildKey(string queueId, string recipientEmail) => $"{PostfixDeliveryEvent.NormalizeQueueId(queueId)}::{PostfixDeliveryEvent.NormalizeEmail(recipientEmail)}";
}

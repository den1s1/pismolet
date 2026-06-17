using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryRiskCheckRepository : IRiskCheckRepository
{
    private readonly ConcurrentDictionary<Guid, RiskCheckResult> _items = new();

    public RiskCheckResult? GetByMailingId(Guid mailingId) => _items.GetValueOrDefault(mailingId);

    public void Save(RiskCheckResult result) => _items[result.MailingId] = result;
}

public sealed class InMemoryModerationReviewRepository : IModerationReviewRepository
{
    private readonly ConcurrentDictionary<Guid, ModerationReview> _items = new();

    public ModerationReview? Get(Guid id) => _items.GetValueOrDefault(id);

    public ModerationReview? GetOpenByMailingId(Guid mailingId) => _items.Values.FirstOrDefault(review => review.MailingId == mailingId && review.Status == ModerationReviewStatus.Open);

    public IReadOnlyCollection<ModerationReview> ListOpen() => _items.Values
        .Where(review => review.Status == ModerationReviewStatus.Open)
        .OrderBy(review => review.CreatedAt)
        .ToArray();

    public void Save(ModerationReview review) => _items[review.Id] = review;
}

public sealed class InMemoryModerationActionLogRepository : IModerationActionLogRepository
{
    private readonly ConcurrentDictionary<Guid, List<ModerationActionLog>> _items = new();

    public void Add(ModerationActionLog log)
    {
        var list = _items.GetOrAdd(log.ReviewId, _ => new List<ModerationActionLog>());
        lock (list)
        {
            list.Add(log);
        }
    }

    public IReadOnlyCollection<ModerationActionLog> ListForReview(Guid reviewId)
    {
        if (!_items.TryGetValue(reviewId, out var list))
        {
            return Array.Empty<ModerationActionLog>();
        }

        lock (list)
        {
            return list.OrderBy(item => item.CreatedAt).ToArray();
        }
    }
}

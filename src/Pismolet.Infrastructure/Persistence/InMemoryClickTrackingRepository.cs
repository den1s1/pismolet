using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryClickTrackingRepository : IClickTrackingRepository
{
    private readonly ConcurrentDictionary<string, TrackedLink> _linksByToken = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, ClickEvent> _events = new();

    public TrackedLink AddOrGet(TrackedLink trackedLink)
    {
        var normalized = NormalizeToken(trackedLink.Token);
        var item = trackedLink with { Token = normalized };
        return _linksByToken.GetOrAdd(normalized, item);
    }

    public TrackedLink? GetByToken(string token)
    {
        var normalized = NormalizeToken(token);
        return string.IsNullOrWhiteSpace(normalized) ? null : _linksByToken.GetValueOrDefault(normalized);
    }

    public IReadOnlyCollection<TrackedLink> ListLinksByMailingId(Guid mailingId) => _linksByToken.Values
        .Where(link => link.MailingId == mailingId)
        .OrderBy(link => link.RecipientEmail, StringComparer.OrdinalIgnoreCase)
        .ThenBy(link => link.OriginalUrl, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyCollection<ClickEvent> ListEventsByMailingId(Guid mailingId, int limit) => _events.Values
        .Where(click => click.MailingId == mailingId)
        .OrderByDescending(click => click.ClickedAt)
        .Take(Math.Max(1, limit))
        .ToArray();

    public void SaveLink(TrackedLink trackedLink)
    {
        var normalized = NormalizeToken(trackedLink.Token);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _linksByToken[normalized] = trackedLink with { Token = normalized };
    }

    public void AddEvent(ClickEvent clickEvent) => _events[clickEvent.Id == Guid.Empty ? Guid.NewGuid() : clickEvent.Id] = clickEvent;

    private static string NormalizeToken(string token) => token.Trim();
}

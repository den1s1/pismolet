using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfClickTrackingRepository(PismoletDbContext db) : IClickTrackingRepository
{
    public TrackedLink AddOrGet(TrackedLink trackedLink)
    {
        var normalized = NormalizeToken(trackedLink.Token);
        if (db.TrackedLinks.AsNoTracking().FirstOrDefault(x => x.Token == normalized) is { } existing)
        {
            return ToDomain(existing);
        }

        var entity = ToEntity(trackedLink with { Token = normalized });
        db.TrackedLinks.Add(entity);
        db.SaveChanges();
        return ToDomain(entity);
    }

    public TrackedLink? GetByToken(string token)
    {
        var normalized = NormalizeToken(token);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : db.TrackedLinks.AsNoTracking().FirstOrDefault(x => x.Token == normalized) is { } entity
                ? ToDomain(entity)
                : null;
    }

    public IReadOnlyCollection<TrackedLink> ListLinksByMailingId(Guid mailingId) => db.TrackedLinks
        .AsNoTracking()
        .Where(x => x.MailingId == mailingId)
        .OrderBy(x => x.RecipientEmail)
        .ThenBy(x => x.OriginalUrl)
        .Select(x => ToDomain(x))
        .ToArray();

    public IReadOnlyCollection<ClickEvent> ListEventsByMailingId(Guid mailingId, int limit) => db.ClickEvents
        .AsNoTracking()
        .Where(x => x.MailingId == mailingId)
        .OrderByDescending(x => x.ClickedAt)
        .Take(Math.Max(1, limit))
        .Select(x => ToDomain(x))
        .ToArray();

    public void SaveLink(TrackedLink trackedLink)
    {
        var normalized = NormalizeToken(trackedLink.Token);
        var entity = db.TrackedLinks.FirstOrDefault(x => x.Token == normalized);
        if (entity is null)
        {
            db.TrackedLinks.Add(ToEntity(trackedLink with { Token = normalized }));
        }
        else
        {
            entity.MailingId = trackedLink.MailingId;
            entity.RecipientEmail = TrackedLink.NormalizeRecipient(trackedLink.RecipientEmail);
            entity.OriginalUrl = TrackedLink.NormalizeOriginalUrl(trackedLink.OriginalUrl);
            entity.CreatedAt = trackedLink.CreatedAt.ToUniversalTime();
            entity.UpdatedAt = trackedLink.UpdatedAt.ToUniversalTime();
            entity.FirstClickedAt = trackedLink.FirstClickedAt?.ToUniversalTime();
            entity.LastClickedAt = trackedLink.LastClickedAt?.ToUniversalTime();
            entity.ClickCount = trackedLink.ClickCount;
        }

        db.SaveChanges();
    }

    public void AddEvent(ClickEvent clickEvent)
    {
        db.ClickEvents.Add(ToEntity(clickEvent));
        db.SaveChanges();
    }

    private static TrackedLinkEntity ToEntity(TrackedLink item) => new()
    {
        Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
        MailingId = item.MailingId,
        RecipientEmail = TrackedLink.NormalizeRecipient(item.RecipientEmail),
        Token = NormalizeToken(item.Token),
        OriginalUrl = TrackedLink.NormalizeOriginalUrl(item.OriginalUrl),
        CreatedAt = item.CreatedAt.ToUniversalTime(),
        UpdatedAt = item.UpdatedAt.ToUniversalTime(),
        FirstClickedAt = item.FirstClickedAt?.ToUniversalTime(),
        LastClickedAt = item.LastClickedAt?.ToUniversalTime(),
        ClickCount = item.ClickCount
    };

    private static TrackedLink ToDomain(TrackedLinkEntity item) => new(
        item.Id,
        item.MailingId,
        item.RecipientEmail,
        item.Token,
        item.OriginalUrl,
        item.CreatedAt,
        item.UpdatedAt,
        item.FirstClickedAt,
        item.LastClickedAt,
        item.ClickCount);

    private static ClickEventEntity ToEntity(ClickEvent item) => new()
    {
        Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
        TrackedLinkId = item.TrackedLinkId,
        MailingId = item.MailingId,
        RecipientEmail = TrackedLink.NormalizeRecipient(item.RecipientEmail),
        Token = NormalizeToken(item.Token),
        OriginalUrl = TrackedLink.NormalizeOriginalUrl(item.OriginalUrl),
        ClickedAt = item.ClickedAt.ToUniversalTime(),
        IpHash = item.IpHash,
        UserAgentHash = item.UserAgentHash
    };

    private static ClickEvent ToDomain(ClickEventEntity item) => new(
        item.Id,
        item.TrackedLinkId,
        item.MailingId,
        item.RecipientEmail,
        item.Token,
        item.OriginalUrl,
        item.ClickedAt,
        item.IpHash,
        item.UserAgentHash);

    private static string NormalizeToken(string token) => token.Trim();
}

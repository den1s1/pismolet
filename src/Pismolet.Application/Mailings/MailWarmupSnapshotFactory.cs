namespace Pismolet.Web.Application.Mailings;

public sealed record MailWarmupAcceptedSend(string RecipientEmail, DateTimeOffset SentAt);

public static class MailWarmupSnapshotFactory
{
    public static MailWarmupLimitSnapshot Build(
        IEnumerable<MailWarmupAcceptedSend> acceptedSends,
        string? recipientEmail,
        DateTimeOffset now)
    {
        var acceptedEvents = acceptedSends
            .Select(x => new SentEventSnapshot(x.RecipientEmail, x.SentAt.ToUniversalTime()))
            .Where(x => x.SentAt <= now.ToUniversalTime())
            .ToArray();
        var recipientDomain = GetEmailDomain(recipientEmail);
        var domainEvents = string.IsNullOrWhiteSpace(recipientDomain)
            ? Array.Empty<SentEventSnapshot>()
            : acceptedEvents.Where(x => string.Equals(GetEmailDomain(x.RecipientEmail), recipientDomain, StringComparison.OrdinalIgnoreCase)).ToArray();

        return new MailWarmupLimitSnapshot(
            GlobalSentLastMinute: CountSince(acceptedEvents, now, TimeSpan.FromMinutes(1)),
            GlobalSentLastHour: CountSince(acceptedEvents, now, TimeSpan.FromHours(1)),
            GlobalSentToday: CountToday(acceptedEvents, now),
            GlobalLastSentAt: LastSentAt(acceptedEvents),
            DomainSentLastMinute: CountSince(domainEvents, now, TimeSpan.FromMinutes(1)),
            DomainSentLastHour: CountSince(domainEvents, now, TimeSpan.FromHours(1)),
            DomainSentToday: CountToday(domainEvents, now),
            DomainLastSentAt: LastSentAt(domainEvents));
    }

    private static int CountSince(IReadOnlyCollection<SentEventSnapshot> events, DateTimeOffset now, TimeSpan window)
    {
        var from = now.ToUniversalTime() - window;
        var until = now.ToUniversalTime();
        return events.Count(x => x.SentAt > from && x.SentAt <= until);
    }

    private static int CountToday(IReadOnlyCollection<SentEventSnapshot> events, DateTimeOffset now)
    {
        var today = now.ToUniversalTime().UtcDateTime.Date;
        return events.Count(x => x.SentAt.UtcDateTime.Date == today);
    }

    private static DateTimeOffset? LastSentAt(IReadOnlyCollection<SentEventSnapshot> events) => events
        .OrderByDescending(x => x.SentAt)
        .Select(x => (DateTimeOffset?)x.SentAt)
        .FirstOrDefault();

    private static string? GetEmailDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.LastIndexOf('@');
        return at < 0 || at >= email.Length - 1
            ? null
            : email[(at + 1)..].Trim().ToLowerInvariant();
    }

    private sealed record SentEventSnapshot(string RecipientEmail, DateTimeOffset SentAt);
}

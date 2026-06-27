using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record MailWarmupAcceptedSend(string RecipientEmail, DateTimeOffset SentAt)
{
    public static MailWarmupAcceptedSend? FromSendEvent(SendEvent sendEvent)
    {
        return sendEvent.Status == SendEventStatus.Accepted && sendEvent.AcceptedAt is { } acceptedAt
            ? new MailWarmupAcceptedSend(sendEvent.RecipientEmail, acceptedAt)
            : null;
    }
}

public static class MailWarmupSnapshotFactory
{
    public static MailWarmupLimitSnapshot Build(
        IEnumerable<MailWarmupAcceptedSend> acceptedSends,
        string? recipientEmail,
        DateTimeOffset now)
    {
        var nowUtc = now.ToUniversalTime();
        var acceptedEvents = acceptedSends
            .Select(x => x with { SentAt = x.SentAt.ToUniversalTime() })
            .Where(x => x.SentAt <= nowUtc)
            .ToArray();
        var recipientDomain = GetEmailDomain(recipientEmail);
        var domainEvents = string.IsNullOrWhiteSpace(recipientDomain)
            ? Array.Empty<MailWarmupAcceptedSend>()
            : acceptedEvents.Where(x => string.Equals(GetEmailDomain(x.RecipientEmail), recipientDomain, StringComparison.OrdinalIgnoreCase)).ToArray();

        return new MailWarmupLimitSnapshot(
            GlobalSentLastMinute: CountSince(acceptedEvents, nowUtc, TimeSpan.FromMinutes(1)),
            GlobalSentLastHour: CountSince(acceptedEvents, nowUtc, TimeSpan.FromHours(1)),
            GlobalSentToday: CountToday(acceptedEvents, nowUtc),
            GlobalLastSentAt: LastSentAt(acceptedEvents),
            DomainSentLastMinute: CountSince(domainEvents, nowUtc, TimeSpan.FromMinutes(1)),
            DomainSentLastHour: CountSince(domainEvents, nowUtc, TimeSpan.FromHours(1)),
            DomainSentToday: CountToday(domainEvents, nowUtc),
            DomainLastSentAt: LastSentAt(domainEvents),
            GlobalOldestSentLastMinuteAt: OldestSince(acceptedEvents, nowUtc, TimeSpan.FromMinutes(1)),
            GlobalOldestSentLastHourAt: OldestSince(acceptedEvents, nowUtc, TimeSpan.FromHours(1)),
            DomainOldestSentLastMinuteAt: OldestSince(domainEvents, nowUtc, TimeSpan.FromMinutes(1)),
            DomainOldestSentLastHourAt: OldestSince(domainEvents, nowUtc, TimeSpan.FromHours(1)));
    }

    private static int CountSince(IReadOnlyCollection<MailWarmupAcceptedSend> sends, DateTimeOffset nowUtc, TimeSpan window)
    {
        var from = nowUtc - window;
        return sends.Count(x => x.SentAt > from && x.SentAt <= nowUtc);
    }

    private static int CountToday(IReadOnlyCollection<MailWarmupAcceptedSend> sends, DateTimeOffset nowUtc)
    {
        var today = nowUtc.UtcDateTime.Date;
        return sends.Count(x => x.SentAt.UtcDateTime.Date == today);
    }

    private static DateTimeOffset? LastSentAt(IReadOnlyCollection<MailWarmupAcceptedSend> sends) => sends
        .OrderByDescending(x => x.SentAt)
        .Select(x => (DateTimeOffset?)x.SentAt)
        .FirstOrDefault();

    private static DateTimeOffset? OldestSince(IReadOnlyCollection<MailWarmupAcceptedSend> sends, DateTimeOffset nowUtc, TimeSpan window)
    {
        var from = nowUtc - window;
        return sends
            .Where(x => x.SentAt > from && x.SentAt <= nowUtc)
            .OrderBy(x => x.SentAt)
            .Select(x => (DateTimeOffset?)x.SentAt)
            .FirstOrDefault();
    }

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
}

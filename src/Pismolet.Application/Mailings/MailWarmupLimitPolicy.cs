namespace Pismolet.Web.Application.Mailings;

public static class MailWarmupLimitPolicy
{
    public static MailWarmupLimitDecision Evaluate(
        MailWarmupLimitOptions options,
        MailWarmupLimitSnapshot snapshot,
        string? recipientEmail,
        DateTimeOffset now)
    {
        var domainLimit = options.GetDomainLimit(GetEmailDomain(recipientEmail));

        return FirstBlocked(
            (options.MaxPerDay, snapshot.GlobalSentToday, "global_daily_limit", TimeUntilNextUtcDay(now)),
            (options.MaxPerHour, snapshot.GlobalSentLastHour, "global_hourly_limit", TimeSpan.FromHours(1)),
            (options.MaxPerMinute, snapshot.GlobalSentLastMinute, "global_minute_limit", TimeSpan.FromMinutes(1)),
            (domainLimit.MaxPerDay ?? 0, snapshot.DomainSentToday, "domain_daily_limit", TimeUntilNextUtcDay(now)),
            (domainLimit.MaxPerHour ?? 0, snapshot.DomainSentLastHour, "domain_hourly_limit", TimeSpan.FromHours(1)),
            (domainLimit.MaxPerMinute ?? 0, snapshot.DomainSentLastMinute, "domain_minute_limit", TimeSpan.FromMinutes(1)))
            ?? DelayDecision("global_min_delay", snapshot.GlobalLastSentAt, options.MinSecondsBetweenSends, now)
            ?? DelayDecision("domain_min_delay", snapshot.DomainLastSentAt, domainLimit.MinSecondsBetweenSends ?? options.MinSecondsBetweenSends, now)
            ?? MailWarmupLimitDecision.Allowed;
    }

    private static MailWarmupLimitDecision? FirstBlocked(params (int Limit, int Current, string Reason, TimeSpan RetryAfter)[] checks)
    {
        foreach (var check in checks)
        {
            if (check.Limit > 0 && check.Current >= check.Limit)
            {
                return MailWarmupLimitDecision.Blocked(check.Reason, check.RetryAfter);
            }
        }

        return null;
    }

    private static MailWarmupLimitDecision? DelayDecision(string reason, DateTimeOffset? lastSentAt, int minSecondsBetweenSends, DateTimeOffset now)
    {
        if (lastSentAt is null || minSecondsBetweenSends <= 0)
        {
            return null;
        }

        var retryAfter = TimeSpan.FromSeconds(minSecondsBetweenSends) - (now - lastSentAt.Value);
        return retryAfter > TimeSpan.Zero ? MailWarmupLimitDecision.Blocked(reason, retryAfter) : null;
    }

    private static TimeSpan TimeUntilNextUtcDay(DateTimeOffset now) =>
        now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime;

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

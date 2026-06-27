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
            (options.MaxPerHour, snapshot.GlobalSentLastHour, "global_hourly_limit", TimeUntilWindowClears(snapshot.GlobalOldestSentLastHourAt, now, TimeSpan.FromHours(1))),
            (options.MaxPerMinute, snapshot.GlobalSentLastMinute, "global_minute_limit", TimeUntilWindowClears(snapshot.GlobalOldestSentLastMinuteAt, now, TimeSpan.FromMinutes(1))),
            (domainLimit.MaxPerDay ?? 0, snapshot.DomainSentToday, "domain_daily_limit", TimeUntilNextUtcDay(now)),
            (domainLimit.MaxPerHour ?? 0, snapshot.DomainSentLastHour, "domain_hourly_limit", TimeUntilWindowClears(snapshot.DomainOldestSentLastHourAt, now, TimeSpan.FromHours(1))),
            (domainLimit.MaxPerMinute ?? 0, snapshot.DomainSentLastMinute, "domain_minute_limit", TimeUntilWindowClears(snapshot.DomainOldestSentLastMinuteAt, now, TimeSpan.FromMinutes(1))))
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

    private static TimeSpan TimeUntilNextUtcDay(DateTimeOffset now) =>
        now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime;

    private static TimeSpan TimeUntilWindowClears(DateTimeOffset? oldestSentAt, DateTimeOffset now, TimeSpan window)
    {
        if (oldestSentAt is null)
        {
            return window;
        }

        var retryAfter = oldestSentAt.Value.ToUniversalTime().Add(window) - now.ToUniversalTime();
        return retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.Zero;
    }

    private static string? GetEmailDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.LastIndexOf(Convert.ToChar(64));
        return at < 0 || at >= email.Length - 1
            ? null
            : email[(at + 1)..].Trim().ToLowerInvariant();
    }
}

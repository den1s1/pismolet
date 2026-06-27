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
            ?? MinDelayBlocked(options, domainLimit, snapshot, now)
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

    private static MailWarmupLimitDecision? MinDelayBlocked(
        MailWarmupLimitOptions options,
        DomainMailWarmupLimitOptions domainLimit,
        MailWarmupLimitSnapshot snapshot,
        DateTimeOffset now)
    {
        var candidates = new[]
            {
                MinDelayCandidate(options.MinSecondsBetweenSends, snapshot.GlobalLastSentAt, "global_min_send_interval", now),
                MinDelayCandidate(domainLimit.MinSecondsBetweenSends, snapshot.DomainLastSentAt, "domain_min_send_interval", now)
            }
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderByDescending(x => x.RetryAfter)
            .ToArray();

        return candidates.Length == 0 ? null : candidates[0];
    }

    private static MailWarmupLimitDecision? MinDelayCandidate(
        int? minSeconds,
        DateTimeOffset? lastSentAt,
        string reason,
        DateTimeOffset now)
    {
        if (minSeconds is null or <= 0 || lastSentAt is null)
        {
            return null;
        }

        var retryAfter = lastSentAt.Value.ToUniversalTime().AddSeconds(minSeconds.Value) - now.ToUniversalTime();
        return retryAfter > TimeSpan.Zero
            ? MailWarmupLimitDecision.Blocked(reason, retryAfter)
            : null;
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

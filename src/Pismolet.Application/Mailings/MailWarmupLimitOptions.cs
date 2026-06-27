namespace Pismolet.Web.Application.Mailings;

public sealed record MailWarmupLimitOptions(
    int MaxPerMinute = 1,
    int MaxPerHour = 20,
    int MaxPerDay = 50,
    int MinSecondsBetweenSends = 30,
    IReadOnlyDictionary<string, DomainMailWarmupLimitOptions>? DomainLimits = null)
{
    public static MailWarmupLimitOptions Default { get; } = new();

    public DomainMailWarmupLimitOptions GetDomainLimit(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || DomainLimits is null)
        {
            return DomainMailWarmupLimitOptions.FromGlobal(this);
        }

        var normalizedDomain = domain.Trim().ToLowerInvariant();

        if (DomainLimits.TryGetValue(normalizedDomain, out var directLimit))
        {
            return directLimit.WithGlobalFallback(this);
        }

        foreach (var entry in DomainLimits)
        {
            if (string.Equals(entry.Key.Trim(), normalizedDomain, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value.WithGlobalFallback(this);
            }
        }

        return DomainMailWarmupLimitOptions.FromGlobal(this);
    }
}

public sealed record DomainMailWarmupLimitOptions(
    int? MaxPerMinute = null,
    int? MaxPerHour = null,
    int? MaxPerDay = null,
    int? MinSecondsBetweenSends = null)
{
    public static DomainMailWarmupLimitOptions FromGlobal(MailWarmupLimitOptions options) => new(
        options.MaxPerMinute,
        options.MaxPerHour,
        options.MaxPerDay,
        options.MinSecondsBetweenSends);

    public DomainMailWarmupLimitOptions WithGlobalFallback(MailWarmupLimitOptions options) => new(
        MaxPerMinute ?? options.MaxPerMinute,
        MaxPerHour ?? options.MaxPerHour,
        MaxPerDay ?? options.MaxPerDay,
        MinSecondsBetweenSends ?? options.MinSecondsBetweenSends);
}

public sealed record MailWarmupLimitSnapshot(
    int GlobalSentLastMinute,
    int GlobalSentLastHour,
    int GlobalSentToday,
    DateTimeOffset? GlobalLastSentAt,
    int DomainSentLastMinute = 0,
    int DomainSentLastHour = 0,
    int DomainSentToday = 0,
    DateTimeOffset? DomainLastSentAt = null,
    DateTimeOffset? GlobalOldestSentLastMinuteAt = null,
    DateTimeOffset? GlobalOldestSentLastHourAt = null,
    DateTimeOffset? DomainOldestSentLastMinuteAt = null,
    DateTimeOffset? DomainOldestSentLastHourAt = null);

public sealed record MailWarmupLimitDecision(
    bool IsAllowed,
    string Reason,
    TimeSpan RetryAfter)
{
    public static MailWarmupLimitDecision Allowed { get; } = new(true, "allowed", TimeSpan.Zero);

    public static MailWarmupLimitDecision Blocked(string reason, TimeSpan retryAfter) =>
        new(false, reason, retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);
}

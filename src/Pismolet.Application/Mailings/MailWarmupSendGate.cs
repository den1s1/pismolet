using Pismolet.Web.Application.Persistence;

namespace Pismolet.Web.Application.Mailings;

public interface IMailWarmupSendGate
{
    MailWarmupLimitDecision Evaluate(string ownerEmail, string recipientEmail, DateTimeOffset now);
}

public interface IMailWarmupLimitOptionsProvider
{
    MailWarmupLimitOptions GetCurrent();
}

public sealed class StaticMailWarmupLimitOptionsProvider(MailWarmupLimitOptions options) : IMailWarmupLimitOptionsProvider
{
    public MailWarmupLimitOptions GetCurrent() => options;
}

public sealed class RuntimeMailWarmupLimitOptionsProvider(
    IMailWarmupRuntimeSettingsRepository runtimeSettings,
    MailWarmupLimitOptions configuredOptions) : IMailWarmupLimitOptionsProvider
{
    public MailWarmupLimitOptions GetCurrent()
    {
        var settings = runtimeSettings.Get();
        return configuredOptions with
        {
            MaxPerMinute = settings.MaxPerMinute,
            MaxPerHour = settings.MaxPerHour,
            MaxPerDay = settings.MaxPerDay,
            MinSecondsBetweenSends = settings.MinSecondsBetweenSends
        };
    }
}

public sealed class MailWarmupSendGate(
    ISendEventRepository sendEvents,
    IMailWarmupThrottle throttle,
    IMailWarmupLimitOptionsProvider optionsProvider) : IMailWarmupSendGate
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(1);

    public MailWarmupLimitDecision Evaluate(string ownerEmail, string recipientEmail, DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var options = optionsProvider.GetCurrent();
        var acceptedSends = sendEvents.ListAcceptedForWarmupWindow(ownerEmail, utcNow - HistoryWindow).ToArray();
        var policyDecision = throttle.Evaluate(options, acceptedSends, recipientEmail, utcNow);
        if (!policyDecision.IsAllowed)
        {
            return policyDecision;
        }

        return EvaluateMinimumDelay(options, acceptedSends, recipientEmail, utcNow) ?? policyDecision;
    }

    private MailWarmupLimitDecision? EvaluateMinimumDelay(
        MailWarmupLimitOptions options,
        IReadOnlyCollection<MailWarmupAcceptedSend> acceptedSends,
        string recipientEmail,
        DateTimeOffset utcNow)
    {
        var snapshot = MailWarmupSnapshotFactory.Build(acceptedSends, recipientEmail, utcNow);
        var domainLimit = options.GetDomainLimit(GetEmailDomain(recipientEmail));
        var globalDecision = MinimumDelayDecision(options.MinSecondsBetweenSends, snapshot.GlobalLastSentAt, "global_min_send_interval", utcNow);
        var domainDecision = MinimumDelayDecision(domainLimit.MinSecondsBetweenSends, snapshot.DomainLastSentAt, "domain_min_send_interval", utcNow);

        if (globalDecision is null)
        {
            return domainDecision;
        }

        if (domainDecision is null)
        {
            return globalDecision;
        }

        return domainDecision.RetryAfter > globalDecision.RetryAfter ? domainDecision : globalDecision;
    }

    private static MailWarmupLimitDecision? MinimumDelayDecision(
        int? minSeconds,
        DateTimeOffset? lastSentAt,
        string reason,
        DateTimeOffset utcNow)
    {
        if (minSeconds is null or <= 0 || lastSentAt is null)
        {
            return null;
        }

        var retryAfter = lastSentAt.Value.ToUniversalTime().AddSeconds(minSeconds.Value) - utcNow;
        return retryAfter > TimeSpan.Zero
            ? MailWarmupLimitDecision.Blocked(reason, retryAfter)
            : null;
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

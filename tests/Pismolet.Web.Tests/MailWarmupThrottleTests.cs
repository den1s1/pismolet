using Pismolet.Web.Application.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailWarmupThrottleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");

    [Fact]
    public void Throttle_allows_when_snapshot_is_below_limits()
    {
        var throttle = new MailWarmupThrottle();

        var decision = throttle.Evaluate(
            MailWarmupLimitOptions.Default,
            Array.Empty<MailWarmupAcceptedSend>(),
            "lead@gmail.com",
            Now);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allowed", decision.Reason);
    }

    [Fact]
    public void Throttle_blocks_by_domain_daily_limit_from_accepted_sends()
    {
        var throttle = new MailWarmupThrottle();
        var options = new MailWarmupLimitOptions(
            MaxPerMinute: 100,
            MaxPerHour: 100,
            MaxPerDay: 100,
            MinSecondsBetweenSends: 0,
            DomainLimits: new Dictionary<string, DomainMailWarmupLimitOptions>
            {
                ["gmail.com"] = new(MaxPerDay: 2)
            });
        var acceptedSends = new[]
        {
            new MailWarmupAcceptedSend("first@gmail.com", Now.AddHours(-2)),
            new MailWarmupAcceptedSend("second@GMAIL.com", Now.AddHours(-1)),
            new MailWarmupAcceptedSend("other@example.test", Now.AddMinutes(-10))
        };

        var decision = throttle.Evaluate(options, acceptedSends, "target@gmail.com", Now);

        Assert.False(decision.IsAllowed);
        Assert.Equal("domain_daily_limit", decision.Reason);
    }

    [Fact]
    public void Throttle_uses_strictest_delay_from_snapshot_and_policy()
    {
        var throttle = new MailWarmupThrottle();
        var options = new MailWarmupLimitOptions(
            MaxPerMinute: 100,
            MaxPerHour: 100,
            MaxPerDay: 100,
            MinSecondsBetweenSends: 30,
            DomainLimits: new Dictionary<string, DomainMailWarmupLimitOptions>
            {
                ["gmail.com"] = new(MinSecondsBetweenSends: 300)
            });
        var acceptedSends = new[]
        {
            new MailWarmupAcceptedSend("lead@gmail.com", Now.AddSeconds(-10))
        };

        var decision = throttle.Evaluate(options, acceptedSends, "target@gmail.com", Now);

        Assert.False(decision.IsAllowed);
        Assert.Equal("domain_min_delay", decision.Reason);
        Assert.Equal(TimeSpan.FromSeconds(290), decision.RetryAfter);
    }
}

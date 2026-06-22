using Pismolet.Web.Application.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailWarmupLimitPolicyTests
{
    [Fact]
    public void Warmup_policy_allows_send_when_limits_are_not_reached()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var snapshot = new MailWarmupLimitSnapshot(
            GlobalSentLastMinute: 0,
            GlobalSentLastHour: 3,
            GlobalSentToday: 10,
            GlobalLastSentAt: now.AddMinutes(-5));

        var decision = MailWarmupLimitPolicy.Evaluate(
            MailWarmupLimitOptions.Default,
            snapshot,
            "lead@example.test",
            now);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allowed", decision.Reason);
    }

    [Fact]
    public void Warmup_policy_blocks_default_daily_limit()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var snapshot = new MailWarmupLimitSnapshot(
            GlobalSentLastMinute: 0,
            GlobalSentLastHour: 10,
            GlobalSentToday: 50,
            GlobalLastSentAt: now.AddMinutes(-5));

        var decision = MailWarmupLimitPolicy.Evaluate(
            MailWarmupLimitOptions.Default,
            snapshot,
            "lead@example.test",
            now);

        Assert.False(decision.IsAllowed);
        Assert.Equal("global_daily_limit", decision.Reason);
        Assert.True(decision.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Warmup_policy_does_not_block_on_default_minimum_delay_between_sends()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var snapshot = new MailWarmupLimitSnapshot(
            GlobalSentLastMinute: 0,
            GlobalSentLastHour: 1,
            GlobalSentToday: 1,
            GlobalLastSentAt: now.AddSeconds(-10));

        var decision = MailWarmupLimitPolicy.Evaluate(
            MailWarmupLimitOptions.Default,
            snapshot,
            "lead@example.test",
            now);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allowed", decision.Reason);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
    }

    [Fact]
    public void Warmup_policy_does_not_block_on_domain_minimum_delay_between_sends()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var options = new MailWarmupLimitOptions(
            MaxPerMinute: 10,
            MaxPerHour: 100,
            MaxPerDay: 1000,
            MinSecondsBetweenSends: 30,
            DomainLimits: new Dictionary<string, DomainMailWarmupLimitOptions>
            {
                ["gmail.com"] = new(MinSecondsBetweenSends: 300)
            });
        var snapshot = new MailWarmupLimitSnapshot(
            GlobalSentLastMinute: 0,
            GlobalSentLastHour: 1,
            GlobalSentToday: 1,
            GlobalLastSentAt: now.AddSeconds(-10),
            DomainLastSentAt: now.AddSeconds(-10));

        var decision = MailWarmupLimitPolicy.Evaluate(
            options,
            snapshot,
            "lead@gmail.com",
            now);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allowed", decision.Reason);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
    }

    [Fact]
    public void Warmup_policy_applies_domain_limit_override()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var options = new MailWarmupLimitOptions(
            MaxPerMinute: 10,
            MaxPerHour: 100,
            MaxPerDay: 1000,
            MinSecondsBetweenSends: 0,
            DomainLimits: new Dictionary<string, DomainMailWarmupLimitOptions>
            {
                ["Gmail.com"] = new(MaxPerDay: 2)
            });
        var snapshot = new MailWarmupLimitSnapshot(
            GlobalSentLastMinute: 0,
            GlobalSentLastHour: 10,
            GlobalSentToday: 10,
            GlobalLastSentAt: now.AddMinutes(-5),
            DomainSentToday: 2);

        var decision = MailWarmupLimitPolicy.Evaluate(
            options,
            snapshot,
            "lead@gmail.com",
            now);

        Assert.False(decision.IsAllowed);
        Assert.Equal("domain_daily_limit", decision.Reason);
    }
}

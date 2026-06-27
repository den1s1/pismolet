using Pismolet.Web.Application.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailWarmupThrottleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");

    [Fact]
    public void Allows_when_history_is_empty()
    {
        var throttle = new MailWarmupThrottle();

        var decision = throttle.Evaluate(
            MailWarmupLimitOptions.Default,
            Array.Empty<MailWarmupAcceptedSend>(),
            "lead",
            Now);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allowed", decision.Reason);
    }

    [Fact]
    public void Does_not_block_by_minimum_delay_at_throttle_level()
    {
        var throttle = new MailWarmupThrottle();
        var options = new MailWarmupLimitOptions(
            MaxPerMinute: 100,
            MaxPerHour: 100,
            MaxPerDay: 100,
            MinSecondsBetweenSends: 30);
        var history = new[]
        {
            new MailWarmupAcceptedSend("previous", Now.AddSeconds(-10))
        };

        var decision = throttle.Evaluate(options, history, "next", Now);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allowed", decision.Reason);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
    }
}

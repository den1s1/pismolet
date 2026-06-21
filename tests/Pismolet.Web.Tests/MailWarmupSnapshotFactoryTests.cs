using Pismolet.Web.Application.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailWarmupSnapshotFactoryTests
{
    [Fact]
    public void Snapshot_factory_counts_accepted_sends_globally_and_by_domain()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var sends = new[]
        {
            Send("lead1@gmail.com", now.AddSeconds(-10)),
            Send("lead2@Gmail.com", now.AddMinutes(-30)),
            Send("lead3@yandex.ru", now.AddMinutes(-59)),
            Send("lead4@gmail.com", now.AddHours(-2))
        };

        var snapshot = MailWarmupSnapshotFactory.Build(sends, "target@gmail.com", now);

        Assert.Equal(1, snapshot.GlobalSentLastMinute);
        Assert.Equal(3, snapshot.GlobalSentLastHour);
        Assert.Equal(4, snapshot.GlobalSentToday);
        Assert.Equal(now.AddSeconds(-10), snapshot.GlobalLastSentAt);
        Assert.Equal(1, snapshot.DomainSentLastMinute);
        Assert.Equal(2, snapshot.DomainSentLastHour);
        Assert.Equal(3, snapshot.DomainSentToday);
        Assert.Equal(now.AddSeconds(-10), snapshot.DomainLastSentAt);
    }

    [Fact]
    public void Snapshot_factory_ignores_future_and_previous_day_for_today_counts()
    {
        var now = DateTimeOffset.Parse("2026-06-21T00:10:00Z");
        var sends = new[]
        {
            Send("lead1@example.test", now.AddMinutes(-5)),
            Send("lead2@example.test", now.AddMinutes(5)),
            Send("lead3@example.test", now.AddMinutes(-20))
        };

        var snapshot = MailWarmupSnapshotFactory.Build(sends, "target@example.test", now);

        Assert.Equal(0, snapshot.GlobalSentLastMinute);
        Assert.Equal(2, snapshot.GlobalSentLastHour);
        Assert.Equal(1, snapshot.GlobalSentToday);
        Assert.Equal(now.AddMinutes(-5), snapshot.GlobalLastSentAt);
        Assert.Equal(1, snapshot.DomainSentToday);
    }

    [Fact]
    public void Snapshot_factory_returns_empty_domain_counts_for_invalid_recipient()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var sends = new[]
        {
            Send("lead1@example.test", now.AddSeconds(-10))
        };

        var snapshot = MailWarmupSnapshotFactory.Build(sends, "missing-at", now);

        Assert.Equal(1, snapshot.GlobalSentToday);
        Assert.Equal(0, snapshot.DomainSentToday);
        Assert.Null(snapshot.DomainLastSentAt);
    }

    [Fact]
    public void Snapshot_factory_uses_explicit_sent_at_instead_of_mutable_update_time()
    {
        var deliveryUpdateTime = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var yesterdaySend = DateTimeOffset.Parse("2026-06-20T10:00:00Z");
        var sends = new[]
        {
            Send("lead1@example.test", yesterdaySend)
        };

        var snapshot = MailWarmupSnapshotFactory.Build(sends, "target@example.test", deliveryUpdateTime);

        Assert.Equal(0, snapshot.GlobalSentLastMinute);
        Assert.Equal(0, snapshot.GlobalSentLastHour);
        Assert.Equal(0, snapshot.GlobalSentToday);
        Assert.Equal(yesterdaySend, snapshot.GlobalLastSentAt);
        Assert.Equal(0, snapshot.DomainSentToday);
        Assert.Equal(yesterdaySend, snapshot.DomainLastSentAt);
    }

    private static MailWarmupAcceptedSend Send(string recipientEmail, DateTimeOffset sentAt) => new(
        recipientEmail.Trim().ToLowerInvariant(),
        sentAt);
}

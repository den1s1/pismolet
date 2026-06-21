using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailWarmupSnapshotFactoryTests
{
    [Fact]
    public void Snapshot_factory_counts_accepted_sends_globally_and_by_domain()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var mailingId = Guid.NewGuid();
        var events = new[]
        {
            Event(mailingId, "lead1@gmail.com", SendEventStatus.Accepted, now.AddSeconds(-10)),
            Event(mailingId, "lead2@Gmail.com", SendEventStatus.Accepted, now.AddMinutes(-30)),
            Event(mailingId, "lead3@yandex.ru", SendEventStatus.Accepted, now.AddMinutes(-59)),
            Event(mailingId, "lead4@gmail.com", SendEventStatus.Accepted, now.AddHours(-2)),
            Event(mailingId, "lead5@gmail.com", SendEventStatus.Failed, now.AddSeconds(-5))
        };

        var snapshot = MailWarmupSnapshotFactory.Build(events, "target@gmail.com", now);

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
        var mailingId = Guid.NewGuid();
        var events = new[]
        {
            Event(mailingId, "lead1@example.test", SendEventStatus.Accepted, now.AddMinutes(-5)),
            Event(mailingId, "lead2@example.test", SendEventStatus.Accepted, now.AddMinutes(5)),
            Event(mailingId, "lead3@example.test", SendEventStatus.Accepted, now.AddMinutes(-20))
        };

        var snapshot = MailWarmupSnapshotFactory.Build(events, "target@example.test", now);

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
        var mailingId = Guid.NewGuid();
        var events = new[]
        {
            Event(mailingId, "lead1@example.test", SendEventStatus.Accepted, now.AddSeconds(-10))
        };

        var snapshot = MailWarmupSnapshotFactory.Build(events, "missing-at", now);

        Assert.Equal(1, snapshot.GlobalSentToday);
        Assert.Equal(0, snapshot.DomainSentToday);
        Assert.Null(snapshot.DomainLastSentAt);
    }

    private static SendEvent Event(Guid mailingId, string recipientEmail, SendEventStatus status, DateTimeOffset updatedAt) => new(
        Guid.NewGuid(),
        mailingId,
        "owner@example.test",
        recipientEmail.Trim().ToLowerInvariant(),
        status,
        SendSkipReason.None,
        SendEvent.FakeProvider,
        status == SendEventStatus.Accepted ? $"provider-{Guid.NewGuid():N}" : null,
        status == SendEventStatus.Accepted ? 1 : 0,
        null,
        null,
        updatedAt.AddMinutes(-1),
        updatedAt,
        status == SendEventStatus.Accepted ? DeliveryStatus.Accepted : DeliveryStatus.NotReported);
}

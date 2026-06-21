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
        Assert.Equal(now.AddSeconds(-10).ToUniversalTime(), snapshot.GlobalLastSentAt);
        Assert.Equal(1, snapshot.DomainSentLastMinute);
        Assert.Equal(2, snapshot.DomainSentLastHour);
        Assert.Equal(3, snapshot.DomainSentToday);
        Assert.Equal(now.AddSeconds(-10).ToUniversalTime(), snapshot.DomainLastSentAt);
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
        Assert.Equal(now.AddMinutes(-5).ToUniversalTime(), snapshot.GlobalLastSentAt);
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
    public void Snapshot_factory_does_not_recount_old_send_as_fresh_when_clock_is_today()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var yesterdaySend = DateTimeOffset.Parse("2026-06-20T10:00:00Z");
        var sends = new[]
        {
            Send("lead1@example.test", yesterdaySend)
        };

        var snapshot = MailWarmupSnapshotFactory.Build(sends, "target@example.test", now);

        Assert.Equal(0, snapshot.GlobalSentLastMinute);
        Assert.Equal(0, snapshot.GlobalSentLastHour);
        Assert.Equal(0, snapshot.GlobalSentToday);
        Assert.Equal(yesterdaySend.ToUniversalTime(), snapshot.GlobalLastSentAt);
        Assert.Equal(0, snapshot.DomainSentToday);
        Assert.Equal(yesterdaySend.ToUniversalTime(), snapshot.DomainLastSentAt);
    }

    [Fact]
    public void MarkAccepted_sets_accepted_at_once_and_delivery_updates_do_not_change_it()
    {
        var sendEvent = SendEvent.Pending(Guid.NewGuid(), "owner@example.test", "lead@example.test")
            .MarkAccepted("provider-1");
        var acceptedAt = sendEvent.AcceptedAt;

        var updated = sendEvent.ApplyDeliveryStatus(DeliveryStatus.Delivered, DateTimeOffset.UtcNow.AddMinutes(10), "delivered");

        Assert.NotNull(acceptedAt);
        Assert.Equal(acceptedAt, updated.AcceptedAt);
    }

    [Fact]
    public void Warmup_accepted_send_mapping_uses_only_accepted_at_not_updated_at()
    {
        var now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");
        var oldAcceptedAt = DateTimeOffset.Parse("2026-06-20T10:00:00Z");
        var sendEvent = new SendEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "owner@example.test",
                "lead@example.test",
                SendEventStatus.Accepted,
                SendSkipReason.None,
                SendEvent.FakeProvider,
                "provider-1",
                1,
                null,
                null,
                oldAcceptedAt.AddMinutes(-1),
                now,
                DeliveryStatus.Delivered,
                now,
                "delivered",
                oldAcceptedAt);

        var acceptedSend = MailWarmupAcceptedSend.FromSendEvent(sendEvent);
        var snapshot = MailWarmupSnapshotFactory.Build(new[] { acceptedSend! }, "target@example.test", now);

        Assert.NotNull(acceptedSend);
        Assert.Equal(oldAcceptedAt.ToUniversalTime(), acceptedSend!.SentAt.ToUniversalTime());
        Assert.Equal(0, snapshot.GlobalSentToday);
        Assert.Equal(0, snapshot.GlobalSentLastHour);
    }

    [Fact]
    public void Warmup_accepted_send_mapping_ignores_accepted_events_without_accepted_at()
    {
        var sendEvent = new SendEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "owner@example.test",
            "lead@example.test",
            SendEventStatus.Accepted,
            SendSkipReason.None,
            SendEvent.FakeProvider,
            "provider-1",
            1,
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);

        var acceptedSend = MailWarmupAcceptedSend.FromSendEvent(sendEvent);

        Assert.Null(acceptedSend);
    }

    private static MailWarmupAcceptedSend Send(string recipientEmail, DateTimeOffset sentAt) => new(
        recipientEmail.Trim().ToLowerInvariant(),
        sentAt);
}

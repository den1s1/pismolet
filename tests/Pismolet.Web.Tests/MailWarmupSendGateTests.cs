using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailWarmupSendGateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-21T12:00:00Z");

    [Fact]
    public void Send_gate_does_not_block_on_minimum_delay_history()
    {
        var sendEvents = new InMemorySendEventRepository();
        sendEvents.Save(AcceptedEvent("owner@example.test", "sent@gmail.com", acceptedAt: Now.AddSeconds(-10), updatedAt: Now));
        var gate = CreateGate(sendEvents, new MailWarmupLimitOptions(
            MaxPerMinute: 100,
            MaxPerHour: 100,
            MaxPerDay: 100,
            MinSecondsBetweenSends: 30));

        var decision = gate.Evaluate("owner@example.test", "target@gmail.com", Now);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allowed", decision.Reason);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
    }

    [Fact]
    public void Send_gate_uses_accepted_at_not_mutable_updated_at()
    {
        var sendEvents = new InMemorySendEventRepository();
        sendEvents.Save(AcceptedEvent("owner@example.test", "old@gmail.com", acceptedAt: Now.AddDays(-2), updatedAt: Now));
        var gate = CreateGate(sendEvents, new MailWarmupLimitOptions(
            MaxPerMinute: 1,
            MaxPerHour: 1,
            MaxPerDay: 1,
            MinSecondsBetweenSends: 300));

        var decision = gate.Evaluate("owner@example.test", "target@gmail.com", Now);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Send_gate_ignores_other_owner_history()
    {
        var sendEvents = new InMemorySendEventRepository();
        sendEvents.Save(AcceptedEvent("other@example.test", "sent@gmail.com", acceptedAt: Now.AddSeconds(-10), updatedAt: Now));
        var gate = CreateGate(sendEvents, new MailWarmupLimitOptions(
            MaxPerMinute: 1,
            MaxPerHour: 1,
            MaxPerDay: 1,
            MinSecondsBetweenSends: 300));

        var decision = gate.Evaluate("owner@example.test", "target@gmail.com", Now);

        Assert.True(decision.IsAllowed);
    }

    private static MailWarmupSendGate CreateGate(InMemorySendEventRepository sendEvents, MailWarmupLimitOptions options) => new(
        sendEvents,
        new MailWarmupThrottle(),
        options);

    private static SendEvent AcceptedEvent(string ownerEmail, string recipientEmail, DateTimeOffset acceptedAt, DateTimeOffset updatedAt) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        ownerEmail,
        recipientEmail,
        SendEventStatus.Accepted,
        SendSkipReason.None,
        SendEvent.FakeProvider,
        $"provider-{Guid.NewGuid():N}",
        1,
        null,
        null,
        acceptedAt.AddMinutes(-1),
        updatedAt,
        DeliveryStatus.Accepted,
        LastDeliveryEventAt: updatedAt,
        LastDeliverySummary: "delivered",
        AcceptedAt: acceptedAt);
}

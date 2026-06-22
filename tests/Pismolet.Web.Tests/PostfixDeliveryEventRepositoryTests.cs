using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class PostfixDeliveryEventRepositoryTests
{
    [Fact]
    public void In_memory_postfix_repository_keeps_exact_events_idempotent()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var occurredAt = DateTimeOffset.Parse("2026-06-22T13:44:23+00:00");
        var first = PostfixDeliveryEvent.FromParsed(
            "abcdef1234",
            "User@Example.com",
            PostfixDeliveryEventStatus.Sent,
            DeliveryStatus.Delivered,
            "2.0.0",
            "mx.example.com[192.0.2.1]:25",
            "250 2.0.0 Ok",
            occurredAt);
        var second = first with { Id = Guid.NewGuid(), Diagnostic = "duplicate event" };

        var savedFirst = repository.AddIfNotExists(first);
        var savedSecond = repository.AddIfNotExists(second);

        Assert.Equal(savedFirst.Id, savedSecond.Id);
        Assert.Equal("ABCDEF1234", savedFirst.QueueId);
        Assert.Equal("user@example.com", savedFirst.RecipientEmail);
        Assert.Single(repository.ListRecent(10));
    }

    [Fact]
    public void In_memory_postfix_repository_keeps_deferred_and_sent_for_same_queue_recipient()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        repository.AddIfNotExists(PostfixDeliveryEvent.FromParsed("AAAA", "user@example.com", PostfixDeliveryEventStatus.Deferred, DeliveryStatus.SoftBounce, "4.4.1", null, null, DateTimeOffset.Parse("2026-06-22T10:00:00+00:00")));
        repository.AddIfNotExists(PostfixDeliveryEvent.FromParsed("AAAA", "user@example.com", PostfixDeliveryEventStatus.Sent, DeliveryStatus.Delivered, "2.0.0", null, null, DateTimeOffset.Parse("2026-06-22T10:05:00+00:00")));

        var events = repository.ListByRecipient("USER@example.com", 10).ToArray();

        Assert.Equal(2, events.Length);
        Assert.Equal(PostfixDeliveryEventStatus.Sent, events[0].Status);
        Assert.Equal(PostfixDeliveryEventStatus.Deferred, events[1].Status);
    }

    [Fact]
    public void In_memory_postfix_repository_lists_recent_events()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        repository.AddIfNotExists(PostfixDeliveryEvent.FromParsed("AAAA", "a@example.com", PostfixDeliveryEventStatus.Sent, DeliveryStatus.Delivered, "2.0.0", null, null, DateTimeOffset.Parse("2026-06-22T10:00:00+00:00")));
        repository.AddIfNotExists(PostfixDeliveryEvent.FromParsed("BBBB", "b@example.com", PostfixDeliveryEventStatus.Deferred, DeliveryStatus.SoftBounce, "4.4.1", null, null, DateTimeOffset.Parse("2026-06-22T11:00:00+00:00")));

        var recent = repository.ListRecent(1).Single();

        Assert.Equal("BBBB", recent.QueueId);
        Assert.Equal(DeliveryStatus.SoftBounce, recent.DeliveryStatus);
    }
}

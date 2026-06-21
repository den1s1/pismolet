using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class SendEventRepositoryAcceptedAtTests
{
    [Fact]
    public void Ef_send_event_repository_persists_accepted_at_roundtrip()
    {
        using var db = CreateContext();
        var mailingId = Guid.Parse("11111111-aaaa-aaaa-aaaa-111111111111");
        var acceptedAt = DateTimeOffset.Parse("2026-06-20T10:00:00Z");
        db.Mailings.Add(Mailing(mailingId, "owner@example.test", acceptedAt.AddHours(-1)));
        db.SaveChanges();
        var repository = new EfSendEventRepository(db);
        var sendEvent = AcceptedEvent(mailingId, "owner@example.test", "lead@example.test", acceptedAt, updatedAt: acceptedAt.AddDays(1));

        repository.Save(sendEvent);
        db.ChangeTracker.Clear();

        var loaded = repository.Get(mailingId, "lead@example.test");
        var persistedEntity = db.SendEvents.AsNoTracking().Single(x => x.MailingId == mailingId && x.RecipientEmail == "lead@example.test");

        Assert.NotNull(loaded);
        Assert.Equal(acceptedAt, loaded!.AcceptedAt);
        Assert.Equal(acceptedAt.AddDays(1), loaded.UpdatedAt);
        Assert.Equal(20260620, persistedEntity.AcceptedUtcDay);
    }

    [Fact]
    public void Ef_daily_limit_count_uses_accepted_at_not_mutable_updated_at()
    {
        using var db = CreateContext();
        var mailingId = Guid.Parse("22222222-aaaa-aaaa-aaaa-222222222222");
        var acceptedAt = DateTimeOffset.Parse("2026-06-20T23:55:00Z");
        var deliveryUpdatedAt = DateTimeOffset.Parse("2026-06-21T00:10:00Z");
        db.Mailings.Add(Mailing(mailingId, "owner@example.test", acceptedAt.AddHours(-1)));
        db.SaveChanges();
        var repository = new EfSendEventRepository(db);
        repository.Save(AcceptedEvent(mailingId, "owner@example.test", "lead@example.test", acceptedAt, deliveryUpdatedAt));
        db.ChangeTracker.Clear();

        var previousDayCount = repository.CountAcceptedForOwnerOnUtcDate("owner@example.test", new DateOnly(2026, 6, 20));
        var todayCount = repository.CountAcceptedForOwnerOnUtcDate("owner@example.test", new DateOnly(2026, 6, 21));

        Assert.Equal(1, previousDayCount);
        Assert.Equal(0, todayCount);
    }

    [Fact]
    public void Ef_warmup_history_uses_accepted_at_not_mutable_updated_at()
    {
        using var db = CreateContext();
        var mailingId = Guid.Parse("44444444-aaaa-aaaa-aaaa-444444444444");
        var since = DateTimeOffset.Parse("2026-06-21T00:00:00Z");
        var oldAcceptedAt = DateTimeOffset.Parse("2026-06-20T23:55:00Z");
        var recentAcceptedAt = DateTimeOffset.Parse("2026-06-21T00:05:00Z");
        var deliveryUpdatedAt = DateTimeOffset.Parse("2026-06-21T00:10:00Z");
        db.Mailings.Add(Mailing(mailingId, "owner@example.test", oldAcceptedAt.AddHours(-1)));
        db.SaveChanges();
        var repository = new EfSendEventRepository(db);
        repository.Save(AcceptedEvent(mailingId, "owner@example.test", "old@example.test", oldAcceptedAt, deliveryUpdatedAt));
        repository.Save(AcceptedEvent(mailingId, "owner@example.test", "recent@example.test", recentAcceptedAt, deliveryUpdatedAt));
        repository.Save(AcceptedEvent(mailingId, "other@example.test", "other@example.test", recentAcceptedAt, deliveryUpdatedAt));
        db.ChangeTracker.Clear();

        var history = repository.ListAcceptedForWarmupWindow("owner@example.test", since);

        var item = Assert.Single(history);
        Assert.Equal("recent@example.test", item.RecipientEmail);
        Assert.Equal(recentAcceptedAt, item.SentAt);
        Assert.DoesNotContain(history, x => x.RecipientEmail == "old@example.test");
    }

    [Fact]
    public void In_memory_daily_limit_count_uses_accepted_at_not_mutable_updated_at()
    {
        var mailingId = Guid.Parse("33333333-aaaa-aaaa-aaaa-333333333333");
        var acceptedAt = DateTimeOffset.Parse("2026-06-20T23:55:00Z");
        var deliveryUpdatedAt = DateTimeOffset.Parse("2026-06-21T00:10:00Z");
        var repository = new InMemorySendEventRepository();
        repository.Save(AcceptedEvent(mailingId, "owner@example.test", "lead@example.test", acceptedAt, deliveryUpdatedAt));

        var previousDayCount = repository.CountAcceptedForOwnerOnUtcDate("owner@example.test", new DateOnly(2026, 6, 20));
        var todayCount = repository.CountAcceptedForOwnerOnUtcDate("owner@example.test", new DateOnly(2026, 6, 21));

        Assert.Equal(1, previousDayCount);
        Assert.Equal(0, todayCount);
    }

    [Fact]
    public void In_memory_warmup_history_uses_accepted_at_not_mutable_updated_at()
    {
        var mailingId = Guid.Parse("55555555-aaaa-aaaa-aaaa-555555555555");
        var since = DateTimeOffset.Parse("2026-06-21T00:00:00Z");
        var oldAcceptedAt = DateTimeOffset.Parse("2026-06-20T23:55:00Z");
        var recentAcceptedAt = DateTimeOffset.Parse("2026-06-21T00:05:00Z");
        var deliveryUpdatedAt = DateTimeOffset.Parse("2026-06-21T00:10:00Z");
        var repository = new InMemorySendEventRepository();
        repository.Save(AcceptedEvent(mailingId, "owner@example.test", "old@example.test", oldAcceptedAt, deliveryUpdatedAt));
        repository.Save(AcceptedEvent(mailingId, "owner@example.test", "recent@example.test", recentAcceptedAt, deliveryUpdatedAt));
        repository.Save(AcceptedEvent(mailingId, "other@example.test", "other@example.test", recentAcceptedAt, deliveryUpdatedAt));

        var history = repository.ListAcceptedForWarmupWindow("owner@example.test", since);

        var item = Assert.Single(history);
        Assert.Equal("recent@example.test", item.RecipientEmail);
        Assert.Equal(recentAcceptedAt, item.SentAt);
        Assert.DoesNotContain(history, x => x.RecipientEmail == "old@example.test");
    }

    private static PismoletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PismoletDbContext>()
            .UseSqlite($"Data Source=file:send-event-accepted-at-{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        var db = new PismoletDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static SendEvent AcceptedEvent(Guid mailingId, string ownerEmail, string recipientEmail, DateTimeOffset acceptedAt, DateTimeOffset updatedAt) => new(
        Guid.NewGuid(),
        mailingId,
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

    private static MailingEntity Mailing(Guid id, string ownerEmail, DateTimeOffset createdAt) => new()
    {
        Id = id,
        OwnerEmail = ownerEmail,
        Subject = "Warmup test mailing",
        StatusRu = MailingStatus.Sending.ToRu(),
        PublicId = $"PL-{id:N}"[..11].ToUpperInvariant(),
        CreatedAt = createdAt
    };
}

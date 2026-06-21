using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class AdminEfRepositoryTests
{
    [Fact]
    public void Ef_admin_mailing_summary_repository_lists_campaign_and_payment_summaries_from_db_context()
    {
        using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-21T10:00:00+00:00");
        var firstMailingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondMailingId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        db.Users.AddRange(
            User("owner1@example.test", "Клиент Первый", now.AddDays(-3)),
            User("owner2@example.test", "", now.AddDays(-2)));
        db.Mailings.AddRange(
            Mailing(firstMailingId, "owner1@example.test", "Исходная тема", MailingStatus.MessagePrepared, now.AddDays(-2)),
            Mailing(secondMailingId, "owner2@example.test", "Без черновика", MailingStatus.Paid, now.AddDays(-1)));
        db.ImportBatches.AddRange(
            ImportBatch(firstMailingId, now.AddDays(-2), totalRows: 10, accepted: 8),
            ImportBatch(firstMailingId, now.AddDays(-1), totalRows: 150, accepted: 140),
            ImportBatch(secondMailingId, now.AddHours(-10), totalRows: 20, accepted: 19));
        db.MailingMessageDrafts.Add(new MailingMessageDraftEntity
        {
            MailingId = firstMailingId,
            SenderName = "Письмолет",
            Subject = "Тема из черновика",
            Body = "Текст",
            MessageType = nameof(MessageType.Advertising),
            UpdatedAt = now.AddHours(-1)
        });
        db.SaveChanges();

        var repository = new EfAdminMailingSummaryRepository(db);

        var campaignRows = repository.ListSummaries().ToArray();
        var paymentRows = ((IAdminPaymentRepository)repository).ListSummaries().ToArray();

        Assert.Equal(campaignRows.Select(x => x.Id), paymentRows.Select(x => x.Id));
        Assert.Equal([secondMailingId, firstMailingId], campaignRows.Select(x => x.Id).ToArray());

        var first = campaignRows.Single(x => x.Id == firstMailingId);
        Assert.Equal("owner1@example.test", first.OwnerEmail);
        Assert.Equal("Клиент Первый", first.ClientName);
        Assert.Equal("Исходная тема", first.Subject);
        Assert.Equal("Тема из черновика", first.DisplaySubject);
        Assert.Equal(MailingStatus.MessagePrepared, first.Status);
        Assert.Equal(MailingStatus.MessagePrepared.ToRu(), first.StatusRu);
        Assert.Equal(150, first.TotalRows);
        Assert.Equal(140, first.AcceptedRecipients);
        Assert.True(first.HasMessageDraft);

        var second = campaignRows.Single(x => x.Id == secondMailingId);
        Assert.Equal("owner2@example.test", second.OwnerEmail);
        Assert.Equal("", second.ClientName);
        Assert.Equal("Без черновика", second.DisplaySubject);
        Assert.Equal(MailingStatus.Paid, second.Status);
        Assert.Equal(20, second.TotalRows);
        Assert.Equal(19, second.AcceptedRecipients);
        Assert.False(second.HasMessageDraft);
    }

    [Fact]
    public void Ef_admin_recipient_repository_get_profile_loads_recipient_profile_from_db_context()
    {
        using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-21T11:00:00+00:00");
        var firstMailingId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var secondMailingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        const string targetEmail = "target@example.test";

        db.Mailings.AddRange(
            Mailing(firstMailingId, "owner1@example.test", "Первая рассылка", MailingStatus.Sent, now.AddDays(-2)),
            Mailing(secondMailingId, "owner2@example.test", "Вторая рассылка", MailingStatus.Sending, now.AddDays(-1)));
        db.Recipients.AddRange(
            Recipient(firstMailingId, "Target@Example.Test", targetEmail, RecipientStatus.Accepted),
            Recipient(firstMailingId, "other@example.test", "other@example.test", RecipientStatus.Accepted),
            Recipient(secondMailingId, targetEmail, targetEmail, RecipientStatus.Accepted),
            Recipient(secondMailingId, "invalid@example.test", "invalid@example.test", RecipientStatus.Invalid));
        db.SendEvents.Add(new SendEventEntity
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            MailingId = secondMailingId,
            OwnerEmail = "owner2@example.test",
            RecipientEmail = targetEmail,
            Status = nameof(SendEventStatus.Accepted),
            Reason = nameof(SendSkipReason.None),
            Provider = SendEvent.FakeProvider,
            ProviderMessageId = "fake-message-1",
            Attempt = 1,
            DeliveryStatus = nameof(DeliveryStatus.HardBounce),
            LastDeliveryEventAt = now.AddMinutes(-5),
            LastDeliverySummary = "hard bounce",
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now
        });
        db.SaveChanges();

        var repository = new EfAdminRecipientRepository(db);

        var profile = repository.GetProfile(" TARGET@EXAMPLE.TEST ");

        Assert.NotNull(profile);
        Assert.Equal(targetEmail, profile!.Summary.Email);
        Assert.Equal("hard_bounce", profile.Summary.StatusCode);
        Assert.Equal("Hard bounce", profile.Summary.StatusText);
        Assert.Equal(2, profile.Summary.MailingCount);
        Assert.Equal(2, profile.Summary.OwnerCount);
        Assert.Equal(1, profile.Summary.SentCount);
        Assert.Equal(now.AddDays(-2), profile.Summary.FirstSeenAt);
        Assert.Equal(now, profile.Summary.LastMessageAt);

        Assert.Equal(["owner1@example.test", "owner2@example.test"], profile.Owners.Select(x => x.OwnerEmail).ToArray());
        Assert.Equal([secondMailingId, firstMailingId], profile.Mailings.Select(x => x.MailingId).ToArray());
        Assert.Equal(2, profile.Mailings.Single(x => x.MailingId == firstMailingId).AcceptedRecipients);
        Assert.Equal(1, profile.Mailings.Single(x => x.MailingId == secondMailingId).AcceptedRecipients);
        Assert.Equal(now, profile.Mailings.Single(x => x.MailingId == secondMailingId).LastMessageAt);
    }

    [Fact]
    public void Ef_admin_recipient_repository_get_profile_prefers_global_suppression_for_recipient_profile()
    {
        using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-21T11:30:00+00:00");
        var firstMailingId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var secondMailingId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var suppressedAt = now.AddMinutes(-30);
        const string targetEmail = "suppressed@example.test";

        db.Mailings.AddRange(
            Mailing(firstMailingId, "owner1@example.test", "Первая рассылка", MailingStatus.Sent, now.AddDays(-2)),
            Mailing(secondMailingId, "owner2@example.test", "Вторая рассылка", MailingStatus.Sending, now.AddDays(-1)));
        db.Recipients.AddRange(
            Recipient(firstMailingId, "Suppressed@Example.Test", targetEmail, RecipientStatus.Accepted),
            Recipient(firstMailingId, "other@example.test", "other@example.test", RecipientStatus.Accepted),
            Recipient(secondMailingId, targetEmail, targetEmail, RecipientStatus.Accepted),
            Recipient(secondMailingId, targetEmail, targetEmail, RecipientStatus.Invalid));
        db.GlobalSuppressions.Add(new GlobalSuppressionEntity
        {
            Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            EmailNormalized = targetEmail,
            EmailHash = "target-hash",
            Source = nameof(GlobalSuppressionSource.UnsubscribeLink),
            SourceMailingId = firstMailingId,
            SourceRecipientKey = "recipient-key-1",
            CreatedAt = suppressedAt,
            CreatedIpHash = "ip-hash",
            UserAgentHash = "ua-hash"
        });
        db.SaveChanges();

        var repository = new EfAdminRecipientRepository(db);

        var profile = repository.GetProfile(" SUPPRESSED@EXAMPLE.TEST ");

        Assert.NotNull(profile);
        Assert.Equal(targetEmail, profile!.Summary.Email);
        Assert.Equal("unsubscribed", profile.Summary.StatusCode);
        Assert.Equal("Отписался", profile.Summary.StatusText);
        Assert.Equal(2, profile.Summary.MailingCount);
        Assert.Equal(2, profile.Summary.OwnerCount);
        Assert.Equal(suppressedAt, profile.Summary.SuppressedAt);
        Assert.Equal(GlobalSuppressionSource.UnsubscribeLink, profile.Summary.SuppressionSource);

        Assert.Equal(["owner1@example.test", "owner2@example.test"], profile.Owners.Select(x => x.OwnerEmail).ToArray());
        Assert.Equal([secondMailingId, firstMailingId], profile.Mailings.Select(x => x.MailingId).ToArray());
        Assert.Equal(2, profile.Mailings.Single(x => x.MailingId == firstMailingId).AcceptedRecipients);
        Assert.Equal(1, profile.Mailings.Single(x => x.MailingId == secondMailingId).AcceptedRecipients);
    }

    [Fact]
    public void Ef_admin_recipient_repository_get_profile_returns_suppression_only_profile()
    {
        using var db = CreateContext();
        var createdAt = DateTimeOffset.Parse("2026-06-21T12:00:00+00:00");
        const string email = "blocked@example.test";
        db.GlobalSuppressions.Add(new GlobalSuppressionEntity
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            EmailNormalized = email,
            EmailHash = "hash",
            Source = nameof(GlobalSuppressionSource.Admin),
            CreatedAt = createdAt,
            CreatedIpHash = "ip-hash",
            UserAgentHash = "ua-hash"
        });
        db.SaveChanges();

        var repository = new EfAdminRecipientRepository(db);

        var profile = repository.GetProfile(email);

        Assert.NotNull(profile);
        Assert.Equal(email, profile!.Summary.Email);
        Assert.Equal("blocked", profile.Summary.StatusCode);
        Assert.Equal("Заблокирован вручную", profile.Summary.StatusText);
        Assert.Equal(createdAt, profile.Summary.FirstSeenAt);
        Assert.Equal(createdAt, profile.Summary.SuppressedAt);
        Assert.Equal(GlobalSuppressionSource.Admin, profile.Summary.SuppressionSource);
        Assert.Empty(profile.Owners);
        Assert.Empty(profile.Mailings);
    }

    private static PismoletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PismoletDbContext>()
            .UseSqlite($"Data Source=file:admin-ef-tests-{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        var db = new PismoletDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Admin EF tests must use SQLite provider, actual provider is {db.Database.ProviderName}.");
        }

        return db;
    }

    private static UserEntity User(string email, string displayName, DateTimeOffset createdAt) => new()
    {
        Email = email,
        NormalizedEmail = email.ToLowerInvariant(),
        PasswordHash = "hash",
        DisplayName = displayName,
        ConfirmationToken = $"token-{Guid.NewGuid():N}",
        EmailConfirmed = true,
        ProfileStatus = "Active",
        DailySendLimit = 1_000,
        TotalSendLimit = 10_000,
        PremoderationRequired = false,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    private static MailingEntity Mailing(Guid id, string ownerEmail, string subject, MailingStatus status, DateTimeOffset createdAt) => new()
    {
        Id = id,
        OwnerEmail = ownerEmail,
        Subject = subject,
        StatusRu = status.ToRu(),
        PublicId = $"PL-{id:N}"[..11].ToUpperInvariant(),
        CreatedAt = createdAt
    };

    private static ImportBatchEntity ImportBatch(Guid mailingId, DateTimeOffset createdAt, int totalRows, int accepted) => new()
    {
        Id = Guid.NewGuid(),
        MailingId = mailingId,
        FileName = "import.csv",
        SourceFormat = nameof(ImportSourceFormat.Csv),
        CreatedAt = createdAt,
        TotalRows = totalRows,
        Accepted = accepted,
        Duplicates = 0,
        Invalid = 0,
        GloballySuppressed = 0,
        ClientSuppressed = 0,
        Status = nameof(ImportBatchStatus.Completed)
    };

    private static RecipientEntity Recipient(Guid mailingId, string sourceEmail, string normalizedEmail, RecipientStatus status) => new()
    {
        Id = Guid.NewGuid(),
        MailingId = mailingId,
        SourceEmail = sourceEmail,
        NormalizedEmail = normalizedEmail,
        Status = status.ToString(),
        ExclusionReason = status == RecipientStatus.Accepted ? null : "Не принят"
    };
}

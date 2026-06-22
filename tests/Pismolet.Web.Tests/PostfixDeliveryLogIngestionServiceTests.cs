using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class PostfixDeliveryLogIngestionServiceTests
{
    [Fact]
    public void Ingestion_service_parses_and_stores_delivery_events()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var service = CreateService(repository);
        var lines = new[]
        {
            "Jun 22 13:44:23 mail postfix/smtp[12345]: ABCDEF1234: to=<User@Example.com>, relay=mx.example.com[192.0.2.1]:25, delay=1.2, delays=0.01/0.01/0.5/0.68, dsn=2.0.0, status=sent (250 2.0.0 Ok: queued as 12345)",
            "Jun 22 13:44:24 mail postfix/qmgr[12345]: ABCDEF1234: removed"
        };

        var result = service.IngestLines(lines, 2026, TimeSpan.Zero);
        var stored = repository.ListRecent(10).Single();

        Assert.Equal(1, result.Parsed);
        Assert.Equal(1, result.Stored);
        Assert.Equal(1, result.Ignored);
        Assert.Equal(0, result.MatchedSendEvents);
        Assert.Equal(0, result.UpdatedSendEvents);
        Assert.Equal(0, result.ClientSuppressions);
        Assert.Equal("ABCDEF1234", stored.QueueId);
        Assert.Equal("user@example.com", stored.RecipientEmail);
        Assert.Equal(PostfixDeliveryEventStatus.Sent, stored.Status);
        Assert.Equal(DeliveryStatus.Delivered, stored.DeliveryStatus);
        Assert.Equal("2.0.0", stored.Dsn);
        Assert.Contains("queued as", stored.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Ingestion_service_does_not_store_exact_duplicates_twice()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var service = CreateService(repository);
        const string line = "Jun 22 13:45:23 mail postfix/smtp[12345]: 0A1B2C3D4E: to=<user@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=3.1, delays=0.01/0.01/1.5/1.58, dsn=4.4.1, status=deferred (connect to mx.example.com[192.0.2.1]:25: Connection timed out)";

        var first = service.IngestText(line, 2026, TimeSpan.Zero);
        var second = service.IngestText(line, 2026, TimeSpan.Zero);

        Assert.Equal(1, first.Parsed);
        Assert.Equal(1, first.Stored);
        Assert.Equal(1, second.Parsed);
        Assert.Equal(0, second.Stored);
        Assert.Single(repository.ListRecent(10));
    }

    [Fact]
    public void Ingestion_service_keeps_multiple_statuses_for_same_queue_and_recipient()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var service = CreateService(repository);
        var lines = new[]
        {
            "Jun 22 13:45:23 mail postfix/smtp[12345]: 0A1B2C3D4E: to=<user@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=3.1, delays=0.01/0.01/1.5/1.58, dsn=4.4.1, status=deferred (connect to mx.example.com[192.0.2.1]:25: Connection timed out)",
            "Jun 22 13:50:23 mail postfix/smtp[12345]: 0A1B2C3D4E: to=<user@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=1.2, delays=0.01/0.01/0.5/0.68, dsn=2.0.0, status=sent (250 2.0.0 Ok: queued as 12345)"
        };

        var result = service.IngestLines(lines, 2026, TimeSpan.Zero);
        var stored = repository.ListRecent(10);

        Assert.Equal(2, result.Parsed);
        Assert.Equal(2, result.Stored);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, x => x.Status == PostfixDeliveryEventStatus.Deferred && x.DeliveryStatus == DeliveryStatus.SoftBounce);
        Assert.Contains(stored, x => x.Status == PostfixDeliveryEventStatus.Sent && x.DeliveryStatus == DeliveryStatus.Delivered);
    }

    [Fact]
    public void Ingestion_service_applies_delivery_status_to_matching_send_event()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var sendEvents = new FakeSendEventRepository();
        var sendEvent = SendEvent
            .Pending(Guid.Parse("11111111-2222-3333-4444-555555555555"), "owner@example.com", "user@example.com")
            .MarkAccepted("LocalSmtp::0A1B2C3D4E");
        sendEvents.Save(sendEvent);
        var service = CreateService(repository, sendEvents);
        const string line = "Jun 22 13:50:23 mail postfix/smtp[12345]: 0A1B2C3D4E: to=<user@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=1.2, delays=0.01/0.01/0.5/0.68, dsn=2.0.0, status=sent (250 2.0.0 Ok: queued as 12345)";

        var result = service.IngestText(line, 2026, TimeSpan.Zero);
        var updated = sendEvents.Get(sendEvent.MailingId, sendEvent.RecipientEmail)!;

        Assert.Equal(1, result.Parsed);
        Assert.Equal(1, result.Stored);
        Assert.Equal(1, result.MatchedSendEvents);
        Assert.Equal(1, result.UpdatedSendEvents);
        Assert.Equal(0, result.ClientSuppressions);
        Assert.Equal(DeliveryStatus.Delivered, updated.DeliveryStatus);
        Assert.NotNull(updated.LastDeliveryEventAt);
        Assert.Contains("sent", updated.LastDeliverySummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ingestion_service_suppresses_matching_hard_bounce_recipient()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var sendEvents = new FakeSendEventRepository();
        var suppressions = new FakeClientSuppressionRepository();
        var sendEvent = SendEvent
            .Pending(Guid.Parse("11111111-2222-3333-4444-555555555555"), "owner@example.com", "bad@example.com")
            .MarkAccepted("LocalSmtp::0A1B2C3D4E");
        sendEvents.Save(sendEvent);
        var service = CreateService(repository, sendEvents, suppressions);
        const string line = "Jun 22 13:50:23 mail postfix/smtp[12345]: 0A1B2C3D4E: to=<bad@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=1.2, delays=0.01/0.01/0.5/0.68, dsn=5.1.1, status=bounced (550 5.1.1 User unknown)";

        var result = service.IngestText(line, 2026, TimeSpan.Zero);
        var updated = sendEvents.Get(sendEvent.MailingId, sendEvent.RecipientEmail)!;
        var suppression = suppressions.ListRecent(10).Single();

        Assert.Equal(1, result.Parsed);
        Assert.Equal(1, result.Stored);
        Assert.Equal(1, result.MatchedSendEvents);
        Assert.Equal(1, result.UpdatedSendEvents);
        Assert.Equal(1, result.ClientSuppressions);
        Assert.Equal(DeliveryStatus.HardBounce, updated.DeliveryStatus);
        Assert.Equal("owner@example.com", suppression.ClientId);
        Assert.Equal("bad@example.com", suppression.EmailNormalized);
        Assert.Equal(ClientSuppressionReason.HardBounce, suppression.Reason);
        Assert.Equal(sendEvent.MailingId, suppression.SourceMailingId);
        Assert.Equal("0A1B2C3D4E", suppression.SourceProviderMessageId);
    }

    [Fact]
    public void Ingestion_service_does_not_suppress_matching_soft_bounce_recipient()
    {
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var sendEvents = new FakeSendEventRepository();
        var suppressions = new FakeClientSuppressionRepository();
        var sendEvent = SendEvent
            .Pending(Guid.Parse("11111111-2222-3333-4444-555555555555"), "owner@example.com", "temporary@example.com")
            .MarkAccepted("LocalSmtp::0A1B2C3D4E");
        sendEvents.Save(sendEvent);
        var service = CreateService(repository, sendEvents, suppressions);
        const string line = "Jun 22 13:50:23 mail postfix/smtp[12345]: 0A1B2C3D4E: to=<temporary@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=1.2, delays=0.01/0.01/0.5/0.68, dsn=4.2.2, status=deferred (452 4.2.2 Over quota)";

        var result = service.IngestText(line, 2026, TimeSpan.Zero);
        var updated = sendEvents.Get(sendEvent.MailingId, sendEvent.RecipientEmail)!;

        Assert.Equal(1, result.Parsed);
        Assert.Equal(1, result.Stored);
        Assert.Equal(1, result.MatchedSendEvents);
        Assert.Equal(1, result.UpdatedSendEvents);
        Assert.Equal(0, result.ClientSuppressions);
        Assert.Equal(DeliveryStatus.SoftBounce, updated.DeliveryStatus);
        Assert.Empty(suppressions.ListRecent(10));
    }

    private static PostfixDeliveryLogIngestionService CreateService(
        InMemoryPostfixDeliveryEventRepository repository,
        ISendEventRepository? sendEvents = null,
        IClientSuppressionRepository? suppressions = null) => new(
            repository,
            sendEvents ?? new FakeSendEventRepository(),
            suppressions ?? new FakeClientSuppressionRepository());

    private sealed class FakeSendEventRepository : ISendEventRepository
    {
        private readonly List<SendEvent> _items = new();

        public SendEvent? Get(Guid mailingId, string recipientEmail) => _items.FirstOrDefault(x => x.MailingId == mailingId && string.Equals(x.RecipientEmail, Normalize(recipientEmail), StringComparison.OrdinalIgnoreCase));

        public SendEvent? GetByProviderMessageId(string providerMessageId) => _items.FirstOrDefault(x => string.Equals(x.ProviderMessageId, providerMessageId.Trim(), StringComparison.OrdinalIgnoreCase));

        public SendEvent? GetByTrackingToken(string trackingToken) => _items.FirstOrDefault(x => string.Equals(x.TrackingToken, trackingToken.Trim(), StringComparison.OrdinalIgnoreCase));

        public IReadOnlyCollection<SendEvent> ListByMailingId(Guid mailingId) => _items.Where(x => x.MailingId == mailingId).ToArray();

        public IReadOnlyCollection<SendEvent> GetPendingBatch(Guid mailingId, int batchSize) => _items.Where(x => x.MailingId == mailingId && x.Status == SendEventStatus.Pending).Take(batchSize).ToArray();

        public int CountAcceptedForOwnerOnUtcDate(string ownerEmail, DateOnly utcDate) => _items.Count(x => string.Equals(x.OwnerEmail, Normalize(ownerEmail), StringComparison.OrdinalIgnoreCase) && x.Status == SendEventStatus.Accepted && x.AcceptedAt?.UtcDateTime.Date == utcDate.ToDateTime(TimeOnly.MinValue));

        public IReadOnlyCollection<MailWarmupAcceptedSend> ListAcceptedForWarmupWindow(string ownerEmail, DateTimeOffset sinceUtc) => Array.Empty<MailWarmupAcceptedSend>();

        public void Save(SendEvent sendEvent)
        {
            var index = _items.FindIndex(x => x.MailingId == sendEvent.MailingId && string.Equals(x.RecipientEmail, sendEvent.RecipientEmail, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _items[index] = sendEvent;
            }
            else
            {
                _items.Add(sendEvent);
            }
        }

        public MailingSendSummary GetSummary(Guid mailingId, int totalAcceptedRecipients) => MailingSendSummary.Empty(mailingId, totalAcceptedRecipients);

        private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    }

    private sealed class FakeClientSuppressionRepository : IClientSuppressionRepository
    {
        private readonly List<ClientSuppression> _items = new();

        public bool IsSuppressed(string clientId, string normalizedEmail) => _items.Any(x =>
            string.Equals(x.ClientId, Normalize(clientId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.EmailNormalized, Normalize(normalizedEmail), StringComparison.OrdinalIgnoreCase));

        public IReadOnlySet<string> GetSuppressedSet(string clientId, IEnumerable<string> normalizedEmails)
        {
            var normalizedClientId = Normalize(clientId);
            var candidates = normalizedEmails.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _items
                .Where(x => string.Equals(x.ClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase) && candidates.Contains(x.EmailNormalized))
                .Select(x => x.EmailNormalized)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyCollection<ClientSuppression> ListRecent(int limit) => _items
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToArray();

        public ClientSuppression AddOrUpdate(ClientSuppression suppression)
        {
            var normalizedClientId = Normalize(suppression.ClientId);
            var normalizedEmail = Normalize(suppression.EmailNormalized);
            var index = _items.FindIndex(x =>
                string.Equals(x.ClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.EmailNormalized, normalizedEmail, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                var normalized = suppression with { ClientId = normalizedClientId, EmailNormalized = normalizedEmail };
                _items.Add(normalized);
                return normalized;
            }

            var touched = _items[index].Touch(suppression.SourceMailingId, suppression.SourceProviderMessageId);
            _items[index] = touched;
            return touched;
        }

        private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    }
}
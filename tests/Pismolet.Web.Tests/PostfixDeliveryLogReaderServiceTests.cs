using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class PostfixDeliveryLogReaderServiceTests
{
    [Fact]
    public void Reader_initializes_cursor_at_end_when_cursor_is_missing()
    {
        var paths = CreatePaths();
        File.WriteAllText(paths.LogPath, BuildIsoLine("ABCDEF1234", "old@example.com") + Environment.NewLine);
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var service = CreateService(paths, repository, startAtEndWhenCursorMissing: true);

        var result = service.ReadNewLines();

        Assert.True(result.LogExists);
        Assert.True(result.CursorInitialized);
        Assert.Equal(0, result.LinesRead);
        Assert.Empty(repository.ListRecent(10));
        Assert.Equal(new FileInfo(paths.LogPath).Length, long.Parse(File.ReadAllText(paths.CursorPath)));
    }

    [Fact]
    public void Reader_reads_only_new_lines_after_cursor()
    {
        var paths = CreatePaths();
        File.WriteAllText(paths.LogPath, BuildIsoLine("OLDQUEUE1", "old@example.com") + Environment.NewLine);
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var service = CreateService(paths, repository, startAtEndWhenCursorMissing: true);
        _ = service.ReadNewLines();
        File.AppendAllText(paths.LogPath, BuildIsoLine("ABCDEF1234", "user@example.com") + Environment.NewLine);

        var result = service.ReadNewLines();
        var stored = repository.ListRecent(10).Single();

        Assert.Equal(1, result.LinesRead);
        Assert.Equal(1, result.Ingestion.Parsed);
        Assert.Equal(1, result.Ingestion.Stored);
        Assert.Equal("ABCDEF1234", stored.QueueId);
        Assert.Equal("user@example.com", stored.RecipientEmail);
    }

    [Fact]
    public void Reader_applies_delivery_status_to_matching_send_event()
    {
        var paths = CreatePaths();
        const string queueId = "ABCDEF1234";
        File.WriteAllText(paths.LogPath, BuildIsoLine(queueId, "user@example.com") + Environment.NewLine);
        var repository = new InMemoryPostfixDeliveryEventRepository();
        var sendEvents = new FakeSendEventRepository();
        var sendEvent = SendEvent
            .Pending(Guid.Parse("11111111-2222-3333-4444-555555555555"), "owner@example.com", "user@example.com")
            .MarkAccepted($"LocalSmtp::{queueId}");
        sendEvents.Save(sendEvent);
        var service = CreateService(paths, repository, startAtEndWhenCursorMissing: false, sendEvents);

        var result = service.ReadNewLines();
        var updated = sendEvents.Get(sendEvent.MailingId, sendEvent.RecipientEmail)!;

        Assert.Equal(1, result.Ingestion.Parsed);
        Assert.Equal(1, result.Ingestion.MatchedSendEvents);
        Assert.Equal(1, result.Ingestion.UpdatedSendEvents);
        Assert.Equal(DeliveryStatus.Delivered, updated.DeliveryStatus);
    }

    private static PostfixDeliveryLogReaderService CreateService(
        TestPaths paths,
        InMemoryPostfixDeliveryEventRepository repository,
        bool startAtEndWhenCursorMissing,
        ISendEventRepository? sendEvents = null,
        IClientSuppressionRepository? clientSuppressions = null)
    {
        var options = new PostfixDeliveryLogReaderOptions(
            paths.LogPath,
            paths.CursorPath,
            2026,
            TimeSpan.Zero,
            startAtEndWhenCursorMissing);
        var ingestion = new PostfixDeliveryLogIngestionService(
            repository,
            sendEvents ?? new FakeSendEventRepository(),
            clientSuppressions ?? new FakeClientSuppressionRepository());
        return new PostfixDeliveryLogReaderService(options, ingestion);
    }

    private static TestPaths CreatePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pismolet-postfix-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new TestPaths(Path.Combine(directory, "mail.log"), Path.Combine(directory, "postfix.cursor"));
    }

    private static string BuildIsoLine(string queueId, string recipient) =>
        $"2026-06-22T17:01:14.999216+00:00 mail postfix/smtp[54675]: {queueId}: to=<{recipient}>, relay=mx.example.com[192.0.2.1]:25, delay=1.2, delays=0.11/0.01/0.02/1.1, dsn=2.0.0, status=sent (250 OK id=1wbi1J)";

    private sealed record TestPaths(string LogPath, string CursorPath);

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
            var emails = normalizedEmails.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return _items
                .Where(x => string.Equals(x.ClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase) && emails.Contains(x.EmailNormalized))
                .Select(x => x.EmailNormalized)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyCollection<ClientSuppression> ListRecent(int limit) => _items
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToArray();

        public ClientSuppression AddOrUpdate(ClientSuppression suppression)
        {
            var normalized = suppression with
            {
                ClientId = Normalize(suppression.ClientId),
                EmailNormalized = Normalize(suppression.EmailNormalized)
            };

            var index = _items.FindIndex(x =>
                string.Equals(x.ClientId, normalized.ClientId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.EmailNormalized, normalized.EmailNormalized, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                var touched = _items[index].Touch(normalized.SourceMailingId, normalized.SourceProviderMessageId);
                _items[index] = touched;
                return touched;
            }

            _items.Add(normalized);
            return normalized;
        }

        private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    }
}

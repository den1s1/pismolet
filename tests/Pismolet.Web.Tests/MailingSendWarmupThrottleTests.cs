using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailingSendWarmupThrottleTests
{
    [Fact]
    public async Task Execute_batch_delays_without_provider_send_when_warmup_blocks()
    {
        var mailingId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
        var mailings = new InMemoryMailingRepository();
        var sendEvents = new InMemorySendEventRepository();
        var provider = new CountingEmailProviderAdapter();
        var queue = new CountingQueue();
        var audit = new InMemoryAuditLogger();
        var gate = new FixedWarmupGate(MailWarmupLimitDecision.Blocked("global_min_delay", TimeSpan.FromSeconds(20)));
        mailings.TryAdd(SendingMailing(mailingId, "first@example.test", "second@example.test"));
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", "first@example.test"));
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", "second@example.test"));
        var service = CreateService(mailings, sendEvents, provider, queue, audit, gate);

        await service.ExecuteQueuedBatchAsync(mailingId, CancellationToken.None);

        Assert.Equal(0, provider.SendCalls);
        Assert.Equal(1, queue.EnqueueCalls);
        Assert.Equal(TimeSpan.FromSeconds(20), queue.LastDelay);
        Assert.Equal(MailingStatus.Sending, mailings.Get(mailingId)!.Status);
        var events = sendEvents.ListByMailingId(mailingId).ToArray();
        Assert.Contains(events, x => x.RecipientEmail == "first@example.test" && x.Status == SendEventStatus.Pending);
        Assert.Contains(events, x => x.RecipientEmail == "second@example.test" && x.Status == SendEventStatus.Pending);
        Assert.Contains(audit.GetRecords(), x => x.EventType == "mailing_send_delayed_by_warmup" && x.Context.Contains("reason=global_min_delay", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Execute_batch_sends_normally_when_warmup_allows()
    {
        var mailingId = Guid.Parse("bbbbbbbb-2222-2222-2222-bbbbbbbbbbbb");
        var mailings = new InMemoryMailingRepository();
        var sendEvents = new InMemorySendEventRepository();
        var provider = new CountingEmailProviderAdapter();
        var queue = new CountingQueue();
        var audit = new InMemoryAuditLogger();
        var gate = new FixedWarmupGate(MailWarmupLimitDecision.Allowed);
        mailings.TryAdd(SendingMailing(mailingId, "lead@example.test"));
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", "lead@example.test"));
        var service = CreateService(mailings, sendEvents, provider, queue, audit, gate);

        await service.ExecuteQueuedBatchAsync(mailingId, CancellationToken.None);

        Assert.Equal(1, provider.SendCalls);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(MailingStatus.Sent, mailings.Get(mailingId)!.Status);
        var item = Assert.Single(sendEvents.ListByMailingId(mailingId));
        Assert.Equal(SendEventStatus.Accepted, item.Status);
        Assert.NotNull(item.AcceptedAt);
    }

    [Fact]
    public async Task Execute_batch_with_real_warmup_gate_delays_second_recipient_after_first_acceptance()
    {
        var mailingId = Guid.Parse("cccccccc-3333-3333-3333-cccccccccccc");
        var mailings = new InMemoryMailingRepository();
        var sendEvents = new InMemorySendEventRepository();
        var provider = new CountingEmailProviderAdapter();
        var queue = new CountingQueue();
        var audit = new InMemoryAuditLogger();
        var gate = new MailWarmupSendGate(
            sendEvents,
            new MailWarmupThrottle(),
            new MailWarmupLimitOptions(
                MaxPerMinute: 1,
                MaxPerHour: 100,
                MaxPerDay: 1000,
                MinSecondsBetweenSends: 0));
        mailings.TryAdd(SendingMailing(mailingId, "first@example.test", "second@example.test"));
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", "first@example.test"));
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", "second@example.test"));
        var service = CreateService(mailings, sendEvents, provider, queue, audit, gate);

        await service.ExecuteQueuedBatchAsync(mailingId, CancellationToken.None);

        Assert.Equal(1, provider.SendCalls);
        Assert.Equal(1, queue.EnqueueCalls);
        Assert.True(queue.LastDelay > TimeSpan.Zero);
        Assert.Equal(MailingStatus.Sending, mailings.Get(mailingId)!.Status);
        var events = sendEvents.ListByMailingId(mailingId).ToArray();
        Assert.Contains(events, x => x.RecipientEmail == "first@example.test" && x.Status == SendEventStatus.Accepted && x.AcceptedAt is not null);
        Assert.Contains(events, x => x.RecipientEmail == "second@example.test" && x.Status == SendEventStatus.Pending);
        Assert.Contains(audit.GetRecords(), x => x.EventType == "mailing_send_delayed_by_warmup" && x.Context.Contains("reason=global_minute_limit", StringComparison.Ordinal));
    }

    private static MailingSendService CreateService(
        InMemoryMailingRepository mailings,
        InMemorySendEventRepository sendEvents,
        CountingEmailProviderAdapter provider,
        CountingQueue queue,
        InMemoryAuditLogger audit,
        IMailWarmupSendGate gate) => new(
            mailings,
            new InMemoryPaymentRepository(),
            sendEvents,
            new InMemoryGlobalSuppressionRepository(),
            new InMemoryClientSuppressionRepository(),
            new InMemoryUserRepository(),
            provider,
            new EmailNormalizer(),
            new TestUnsubscribeTokenService(),
            new TestInboundReplyTokenService(),
            queue,
            audit,
            new MailingSendOptions(100),
            gate);

    private static Mailing SendingMailing(Guid id, params string[] recipients) => Mailing.Draft("owner@example.test", "Warmup mailing") with
    {
        Id = id,
        Status = MailingStatus.Sending,
        StatusRu = MailingStatus.Sending.ToRu(),
        Recipients = recipients.Select(x => Recipient.Accepted(x, x.Trim().ToLowerInvariant())).ToList(),
        MessageDraft = MailingMessageDraft.Create("Sender", "Subject", "Body", MessageType.Transactional, DateTimeOffset.Parse("2026-06-21T12:00:00Z"))
    };

    private sealed class FixedWarmupGate(MailWarmupLimitDecision decision) : IMailWarmupSendGate
    {
        public MailWarmupLimitDecision Evaluate(string ownerEmail, string recipientEmail, DateTimeOffset now) => decision;
    }

    private sealed class CountingQueue : IBackgroundMailingSendQueue
    {
        public int EnqueueCalls { get; private set; }

        public TimeSpan LastDelay { get; private set; }

        public void Enqueue(Guid mailingId) => Enqueue(mailingId, TimeSpan.Zero);

        public void Enqueue(Guid mailingId, TimeSpan delay)
        {
            EnqueueCalls++;
            LastDelay = delay;
        }
    }

    private sealed class CountingEmailProviderAdapter : IEmailProviderAdapter
    {
        public string ProviderName => SendEvent.FakeProvider;

        public int SendCalls { get; private set; }

        public Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            SendCalls++;
            return Task.FromResult(EmailProviderSendResult.Success($"provider-{message.Recipient.Email}"));
        }

        public Task<EmailProviderWebhookParseResult> ParseWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderWebhookParseResult.Failure("not-supported"));

        public Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderInboundParseResult.Failure("not-supported"));

        public Task<EmailProviderSendResult> ForwardReplyToClientAsync(ReplyEvent replyEvent, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderSendResult.Success($"forward-{replyEvent.Id:N}"));
    }

    private sealed class TestUnsubscribeTokenService : IUnsubscribeTokenService
    {
        public string Generate(Guid mailingId, string recipientEmail, Guid? importBatchId = null) => $"unsubscribe-{recipientEmail}";

        public UnsubscribeTokenValidationResult Validate(string token) => UnsubscribeTokenValidationResult.Failure("not-supported");

        public string BuildRecipientKey(Guid mailingId, string normalizedEmail, Guid? importBatchId = null) => $"key-{normalizedEmail}";
    }

    private sealed class TestInboundReplyTokenService : IInboundReplyTokenService
    {
        public string Generate(Guid mailingId, string clientId, string recipientEmail) => $"reply-{recipientEmail}";

        public InboundReplyTokenValidationResult Validate(string token) => InboundReplyTokenValidationResult.Failure("not-supported");

        public string BuildRecipientKey(Guid mailingId, string normalizedEmail) => $"reply-key-{normalizedEmail}";

        public string BuildReplyToAddress(string token) => $"reply+{token}@reply.example.test";

        public string HashToken(string? token) => token ?? string.Empty;
    }
}

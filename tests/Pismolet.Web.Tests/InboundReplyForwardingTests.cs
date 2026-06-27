using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class InboundReplyForwardingTests
{
    private static readonly Guid MailingId = Guid.Parse("99999999-1111-2222-3333-999999999999");

    [Fact]
    public async Task Matched_inbound_reply_is_queued_and_forwarded_to_client()
    {
        var replies = new InMemoryReplyEventRepository();
        var queue = new CountingReplyQueue();
        var provider = new RecordingReplyProvider();
        var processor = CreateProcessor(replies, provider, queue);
        var inbound = Inbound(headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var result = await processor.ProcessAsync(inbound, Request(), CancellationToken.None);
        Assert.Equal("queued_for_forward", result.Status);
        Assert.NotNull(result.ReplyEventId);
        Assert.Equal(result.ReplyEventId, queue.ForwardedReplyIds.Single());

        await processor.ExecuteForwardAsync(result.ReplyEventId!.Value, CancellationToken.None);

        var saved = replies.Get(result.ReplyEventId.Value);
        Assert.NotNull(saved);
        Assert.Equal(ReplyProcessingStatus.Forwarded, saved!.ProcessingStatus);
        Assert.Equal(1, provider.ForwardCalls);
        var forwarded = Assert.Single(provider.ForwardedReplies);
        Assert.Equal("owner@example.test", forwarded.ForwardToEmailNormalized);
        Assert.Equal("reader@example.test", forwarded.FromEmailNormalized);
        Assert.Equal("Re: Вопрос по письму", forwarded.SubjectPreview);
        Assert.Contains("Спасибо, хочу уточнить детали.", forwarded.BodyTextStored);
    }

    [Fact]
    public async Task Auto_reply_is_stored_but_not_queued_for_forward()
    {
        var replies = new InMemoryReplyEventRepository();
        var queue = new CountingReplyQueue();
        var provider = new RecordingReplyProvider();
        var processor = CreateProcessor(replies, provider, queue);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Auto-Submitted"] = "auto-replied"
        };

        var result = await processor.ProcessAsync(Inbound(headers), Request(), CancellationToken.None);

        Assert.Equal("ignored_auto_reply", result.Status);
        Assert.Empty(queue.ForwardedReplyIds);
        Assert.Equal(0, provider.ForwardCalls);
        var saved = replies.Get(result.ReplyEventId!.Value);
        Assert.NotNull(saved);
        Assert.Equal(ReplyProcessingStatus.IgnoredAutoReply, saved!.ProcessingStatus);
    }

    [Fact]
    public async Task Concurrent_forward_jobs_send_reply_only_once()
    {
        var replies = new InMemoryReplyEventRepository();
        var provider = new BlockingReplyProvider();
        var processor = CreateProcessor(replies, provider, new CountingReplyQueue());
        var reply = replies.AddIfNotExists(QueuedReply());

        var first = processor.ExecuteForwardAsync(reply.Id, CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await processor.ExecuteForwardAsync(reply.Id, CancellationToken.None);
        provider.CompleteSuccess();
        await first;

        Assert.Equal(1, provider.ForwardCalls);
        Assert.Equal(ReplyProcessingStatus.Forwarded, replies.Get(reply.Id)?.ProcessingStatus);
    }

    [Fact]
    public async Task Failed_forward_can_be_retried_without_duplicate_event()
    {
        var replies = new InMemoryReplyEventRepository();
        var provider = new RecordingReplyProvider(failFirst: true);
        var processor = CreateProcessor(replies, provider, new CountingReplyQueue());
        var reply = replies.AddIfNotExists(QueuedReply());

        await processor.ExecuteForwardAsync(reply.Id, CancellationToken.None);
        var failed = replies.Get(reply.Id);
        Assert.NotNull(failed);
        Assert.Equal(ReplyProcessingStatus.Failed, failed!.ProcessingStatus);
        Assert.Equal(1, failed.ForwardRetryCount);

        await processor.ExecuteForwardAsync(reply.Id, CancellationToken.None);

        var forwarded = replies.Get(reply.Id);
        Assert.NotNull(forwarded);
        Assert.Equal(ReplyProcessingStatus.Forwarded, forwarded!.ProcessingStatus);
        Assert.Equal(reply.Id, forwarded.Id);
        Assert.Equal(2, provider.ForwardCalls);
        Assert.Single(provider.ForwardedReplies.Select(x => x.Id).Distinct());
    }

    private static InboundReplyProcessingService CreateProcessor(
        IReplyEventRepository replies,
        IEmailProviderAdapter provider,
        IBackgroundReplyQueue queue) => new(
            replies,
            new FixedMatcher(),
            new FixedReplyTokenService(),
            queue,
            provider,
            new InMemoryAuditLogger(),
            new InboundReplyOptions(14, 12000, 50));

    private static EmailProviderInboundEvent Inbound(IReadOnlyDictionary<string, string> headers) => new(
        Provider: "PostfixSpool",
        ProviderInboundEventId: Guid.NewGuid().ToString("N"),
        FromEmail: "reader@example.test",
        ToAddress: "reply+fixed-token@reply.pismolet.test",
        ReplyToken: "fixed-token",
        Subject: "Re: Вопрос по письму",
        TextBody: "Спасибо, хочу уточнить детали.",
        HtmlBody: null,
        Headers: headers,
        ReceivedAt: DateTimeOffset.Parse("2026-06-27T19:30:00Z"),
        RawPayload: "raw-reply-payload");

    private static ReplyEvent QueuedReply() => ReplyEvent
        .Received(
            "PostfixSpool",
            Guid.NewGuid().ToString("N"),
            "reader@example.test",
            "reply+fixed-token@reply.pismolet.test",
            "token-hash",
            "Re: Вопрос по письму",
            "Спасибо, хочу уточнить детали.",
            DateTimeOffset.Parse("2026-06-27T19:30:00Z"),
            DateTimeOffset.Parse("2026-07-11T19:30:00Z"),
            "raw-hash")
        .MarkMatched(MailingId, "owner@example.test", "reader@example.test", "owner@example.test")
        .MarkQueuedForForward();

    private static RequestMetadata Request() => new("127.0.0.1", "inbound-reply-forwarding-tests");

    private sealed class FixedMatcher : IInboundReplyMatchingService
    {
        public InboundReplyMatchResult Match(EmailProviderInboundEvent inbound)
        {
            var mailing = Mailing.Draft("owner@example.test", "Reply forwarding campaign") with
            {
                Id = MailingId,
                Recipients = new List<Recipient>
                {
                    Recipient.Accepted("reader@example.test", "reader@example.test", rowNumber: 1)
                }
            };

            return InboundReplyMatchResult.Success(mailing, "reader@example.test");
        }
    }

    private sealed class FixedReplyTokenService : IInboundReplyTokenService
    {
        public string Generate(Guid mailingId, string clientId, string recipientEmail) => "fixed-token";

        public InboundReplyTokenValidationResult Validate(string token) => InboundReplyTokenValidationResult.Failure("not-used");

        public string BuildRecipientKey(Guid mailingId, string normalizedEmail) => $"key-{normalizedEmail}";

        public string BuildReplyToAddress(string token) => $"reply+{token}@reply.pismolet.test";

        public string HashToken(string? token) => $"hash-{token}";
    }

    private sealed class CountingReplyQueue : IBackgroundReplyQueue
    {
        private readonly List<Guid> _forwardedReplyIds = new();

        public IReadOnlyCollection<Guid> ForwardedReplyIds => _forwardedReplyIds;

        public void EnqueueForward(Guid replyEventId) => _forwardedReplyIds.Add(replyEventId);

        public void EnqueueCleanup() { }
    }

    private sealed class RecordingReplyProvider(bool failFirst = false) : IEmailProviderAdapter
    {
        private readonly List<ReplyEvent> _forwardedReplies = new();

        public string ProviderName => SendEvent.FakeProvider;

        public int ForwardCalls { get; private set; }

        public IReadOnlyCollection<ReplyEvent> ForwardedReplies => _forwardedReplies;

        public Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderSendResult.Failure("not-supported", "not-supported"));

        public Task<EmailProviderWebhookParseResult> ParseWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderWebhookParseResult.Failure("not-supported"));

        public Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderInboundParseResult.Failure("not-supported"));

        public Task<EmailProviderSendResult> ForwardReplyToClientAsync(ReplyEvent replyEvent, CancellationToken cancellationToken)
        {
            ForwardCalls++;
            if (failFirst && ForwardCalls == 1)
            {
                return Task.FromResult(EmailProviderSendResult.Failure("temporary_forward_failure", "Synthetic forward failure."));
            }

            _forwardedReplies.Add(replyEvent);
            return Task.FromResult(EmailProviderSendResult.Success($"forward-{replyEvent.Id:N}"));
        }
    }

    private sealed class BlockingReplyProvider : IEmailProviderAdapter
    {
        private readonly TaskCompletionSource<EmailProviderSendResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProviderName => SendEvent.FakeProvider;

        public int ForwardCalls { get; private set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderSendResult.Failure("not-supported", "not-supported"));

        public Task<EmailProviderWebhookParseResult> ParseWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderWebhookParseResult.Failure("not-supported"));

        public Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
            Task.FromResult(EmailProviderInboundParseResult.Failure("not-supported"));

        public Task<EmailProviderSendResult> ForwardReplyToClientAsync(ReplyEvent replyEvent, CancellationToken cancellationToken)
        {
            ForwardCalls++;
            Started.TrySetResult();
            return _completion.Task;
        }

        public void CompleteSuccess() => _completion.TrySetResult(EmailProviderSendResult.Success("forward-blocking"));
    }
}

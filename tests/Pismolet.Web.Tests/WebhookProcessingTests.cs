using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mail;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;

namespace Pismolet.Web.Tests;

public sealed class WebhookProcessingTests
{
    [Fact]
    public async Task Fake_provider_parses_delivery_webhook()
    {
        var adapter = new FakeEmailProviderAdapter(new TestFakeMailer());
        var raw = """
        {
          "providerEventId": "evt-1",
          "providerMessageId": "fake-message-1",
          "eventType": "delivered",
          "recipientEmail": "User@Example.Test"
        }
        """;

        var result = await adapter.ParseWebhookAsync(raw, new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(ProviderWebhookEventType.Delivered, result.Event!.EventType);
        Assert.Equal("fake-message-1", result.Event.ProviderMessageId);
    }

    [Fact]
    public void Delivered_webhook_updates_delivery_status_once()
    {
        var mailingId = Guid.NewGuid();
        var sendEvents = new InMemorySendEventRepository();
        var webhookEvents = new InMemoryProviderWebhookEventRepository();
        var clientSuppressions = new InMemoryClientSuppressionRepository();
        var globalSuppressions = new InMemoryGlobalSuppressionRepository();
        var processor = new EmailWebhookProcessingService(sendEvents, webhookEvents, clientSuppressions, globalSuppressions, new EmailNormalizer(), new InMemoryAuditLogger());
        var accepted = SendEvent.Pending(mailingId, "owner@example.test", "user@example.test").MarkAccepted("fake-1");
        sendEvents.Save(accepted);
        var providerEvent = new EmailProviderWebhookEvent(SendEvent.FakeProvider, "evt-1", "fake-1", mailingId, "user@example.test", ProviderWebhookEventType.Delivered, DateTimeOffset.UtcNow, null, null, "{}");

        var first = processor.Process(providerEvent, new RequestMetadata("test", "test"));
        var second = processor.Process(providerEvent, new RequestMetadata("test", "test"));
        var summary = sendEvents.GetSummary(mailingId, 1);

        Assert.True(first.Ok);
        Assert.True(second.Duplicate);
        Assert.Equal(DeliveryStatus.Delivered, sendEvents.Get(mailingId, "user@example.test")!.DeliveryStatus);
        Assert.Equal(1, summary.Delivered);
    }

    [Fact]
    public void Hard_bounce_creates_client_suppression()
    {
        var mailingId = Guid.NewGuid();
        var sendEvents = new InMemorySendEventRepository();
        var webhookEvents = new InMemoryProviderWebhookEventRepository();
        var clientSuppressions = new InMemoryClientSuppressionRepository();
        var globalSuppressions = new InMemoryGlobalSuppressionRepository();
        var processor = new EmailWebhookProcessingService(sendEvents, webhookEvents, clientSuppressions, globalSuppressions, new EmailNormalizer(), new InMemoryAuditLogger());
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", "bad@example.test").MarkAccepted("fake-2"));

        processor.Process(new EmailProviderWebhookEvent(SendEvent.FakeProvider, "evt-2", "fake-2", mailingId, "bad@example.test", ProviderWebhookEventType.HardBounce, DateTimeOffset.UtcNow, "hard", "Mailbox not found", "{}"), new RequestMetadata("test", "test"));

        Assert.Equal(DeliveryStatus.HardBounce, sendEvents.Get(mailingId, "bad@example.test")!.DeliveryStatus);
        Assert.True(clientSuppressions.IsSuppressed("owner@example.test", "bad@example.test"));
    }

    [Fact]
    public void Complaint_creates_global_suppression()
    {
        var mailingId = Guid.NewGuid();
        var sendEvents = new InMemorySendEventRepository();
        var webhookEvents = new InMemoryProviderWebhookEventRepository();
        var clientSuppressions = new InMemoryClientSuppressionRepository();
        var globalSuppressions = new InMemoryGlobalSuppressionRepository();
        var processor = new EmailWebhookProcessingService(sendEvents, webhookEvents, clientSuppressions, globalSuppressions, new EmailNormalizer(), new InMemoryAuditLogger());
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", "complain@example.test").MarkAccepted("fake-3"));

        processor.Process(new EmailProviderWebhookEvent(SendEvent.FakeProvider, "evt-3", "fake-3", mailingId, "complain@example.test", ProviderWebhookEventType.Complaint, DateTimeOffset.UtcNow, "complaint", "Spam complaint", "{}"), new RequestMetadata("test", "test"));

        Assert.Equal(DeliveryStatus.Complaint, sendEvents.Get(mailingId, "complain@example.test")!.DeliveryStatus);
        Assert.True(globalSuppressions.IsSuppressed("complain@example.test"));
    }

    [Fact]
    public void Late_accepted_does_not_override_delivered()
    {
        var mailingId = Guid.NewGuid();
        var item = SendEvent.Pending(mailingId, "owner@example.test", "user@example.test")
            .MarkAccepted("fake-4")
            .ApplyDeliveryStatus(DeliveryStatus.Delivered, DateTimeOffset.UtcNow, "delivered")
            .ApplyDeliveryStatus(DeliveryStatus.Accepted, DateTimeOffset.UtcNow.AddMinutes(1), "accepted");

        Assert.Equal(DeliveryStatus.Delivered, item.DeliveryStatus);
    }

    private sealed class TestFakeMailer : IFakeMailer
    {
        public void SendConfirmation(string to, string subject, string link) { }

        public void AddMailingMessage(
            string to,
            string subject,
            string link,
            string? replyToAddress = null,
            string? replyToken = null,
            string? providerMessageId = null,
            string? textBody = null) { }

        public void AddForwardedReply(string to, string subject, string fromEmail, string textBody, string providerMessageId) { }

        public IReadOnlyCollection<FakeMail> GetOutbox() => Array.Empty<FakeMail>();
    }
}

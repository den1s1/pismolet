using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mail;
using Pismolet.Web.Domain.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class SendingServicesTests
{
    [Fact]
    public void MailingStatus_allows_send_only_from_approved()
    {
        Assert.True(MailingStatus.Approved.CanStartSending());
        Assert.False(MailingStatus.Paid.CanStartSending());
        Assert.False(MailingStatus.Rejected.CanStartSending());
        Assert.False(MailingStatus.Sent.CanStartSending());
    }

    [Fact]
    public async Task Fake_provider_returns_stable_success_id()
    {
        var fakeMailer = new TestFakeMailer();
        var provider = new FakeEmailProviderAdapter(fakeMailer);
        var mailingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var message = CreateMessage(mailingId, "ok@example.test");

        var first = await provider.SendAsync(message, CancellationToken.None);
        var second = await provider.SendAsync(message, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.Equal(first.ProviderMessageId, second.ProviderMessageId);
        Assert.StartsWith("fake-", first.ProviderMessageId);
        Assert.All(fakeMailer.GetOutbox(), item => Assert.Equal("/unsubscribe/test", item.Link));
    }

    [Fact]
    public async Task Fake_provider_returns_failure_for_fail_address()
    {
        var provider = new FakeEmailProviderAdapter(new TestFakeMailer());
        var message = CreateMessage(Guid.NewGuid(), "please-fail@example.test");

        var result = await provider.SendAsync(message, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("fake_failed", result.ErrorCode);
    }

    [Fact]
    public void Send_event_summary_counts_suppression_and_pause()
    {
        var mailingId = Guid.NewGuid();
        var sent = SendEvent.Pending(mailingId, "owner@example.test", "a@example.test").MarkAccepted("fake-1");
        var suppressed = SendEvent.Pending(mailingId, "owner@example.test", "b@example.test").MarkSkipped(SendSkipReason.GlobalSuppression);
        var paused = SendEvent.Pending(mailingId, "owner@example.test", "c@example.test").MarkPaused(SendSkipReason.DailyLimit);

        Assert.Equal(SendEventStatus.Accepted, sent.Status);
        Assert.Equal(SendEventStatus.Skipped, suppressed.Status);
        Assert.Equal(SendEventStatus.Paused, paused.Status);
    }

    private static EmailMessage CreateMessage(Guid mailingId, string recipientEmail) => new(
        mailingId,
        new EmailRecipient(recipientEmail),
        "Sender",
        "Subject",
        "Body",
        "/unsubscribe/test",
        "PL-TEST",
        "reply@example.test",
        "reply-token",
        new Dictionary<string, string> { ["mailingId"] = mailingId.ToString("N") });

    private sealed class TestFakeMailer : IFakeMailer
    {
        private readonly List<FakeMail> _items = new();

        public void SendConfirmation(string to, string subject, string link) => _items.Add(new FakeMail(to, subject, link, DateTimeOffset.UtcNow));

        public void AddMailingMessage(
            string to,
            string subject,
            string link,
            string? replyToAddress = null,
            string? replyToken = null,
            string? providerMessageId = null,
            string? textBody = null) => _items.Add(new FakeMail(to, subject, link, DateTimeOffset.UtcNow));

        public void AddForwardedReply(string to, string subject, string fromEmail, string textBody, string providerMessageId) { }

        public IReadOnlyCollection<FakeMail> GetOutbox() => _items;
    }
}

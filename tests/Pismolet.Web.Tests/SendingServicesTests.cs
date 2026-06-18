using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;

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
        var provider = new FakeEmailProviderAdapter();
        var mailingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var message = new EmailMessage(mailingId, new EmailRecipient("ok@example.test"), "Sender", "Subject", "Body", "/unsubscribe/test", "PL-TEST");

        var first = await provider.SendAsync(message, CancellationToken.None);
        var second = await provider.SendAsync(message, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.Equal(first.ProviderMessageId, second.ProviderMessageId);
        Assert.StartsWith("fake-", first.ProviderMessageId);
    }

    [Fact]
    public async Task Fake_provider_returns_failure_for_fail_address()
    {
        var provider = new FakeEmailProviderAdapter();
        var message = new EmailMessage(Guid.NewGuid(), new EmailRecipient("please-fail@example.test"), "Sender", "Subject", "Body", "/unsubscribe/test", "PL-TEST");

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
}

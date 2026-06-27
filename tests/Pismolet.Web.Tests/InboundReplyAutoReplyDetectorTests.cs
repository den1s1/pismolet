using Pismolet.Web.Application.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class InboundReplyAutoReplyDetectorTests
{
    [Fact]
    public void Detector_ignores_auto_submitted_messages()
    {
        var inbound = BuildInbound(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Auto-Submitted"] = "auto-replied"
        });

        Assert.True(InboundReplyAutoReplyDetector.ShouldIgnore(inbound));
    }

    [Fact]
    public void Detector_ignores_delivery_status_subjects()
    {
        var inbound = BuildInbound(subject: "Delivery Status Notification Failure");

        Assert.True(InboundReplyAutoReplyDetector.ShouldIgnore(inbound));
    }

    [Fact]
    public void Detector_allows_human_reply()
    {
        var inbound = BuildInbound(subject: "Re: Вопрос по рассылке");

        Assert.False(InboundReplyAutoReplyDetector.ShouldIgnore(inbound));
    }

    private static EmailProviderInboundEvent BuildInbound(IReadOnlyDictionary<string, string>? headers = null, string subject = "Re: Test") => new(
        Provider: "PostfixSpool",
        ProviderInboundEventId: Guid.NewGuid().ToString("N"),
        FromEmail: "client@example.test",
        ToAddress: $"reply+token{Convert.ToChar(64)}reply.pismolet.test",
        ReplyToken: "token",
        Subject: subject,
        TextBody: "Reply body",
        HtmlBody: null,
        Headers: headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        ReceivedAt: DateTimeOffset.UtcNow,
        RawPayload: "raw");
}

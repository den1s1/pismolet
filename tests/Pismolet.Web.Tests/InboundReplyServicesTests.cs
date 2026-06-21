using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class InboundReplyServicesTests
{
    [Fact]
    public void Reply_token_uses_inbound_reply_purpose_and_validates()
    {
        var service = CreateService();
        var mailingId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var token = service.Generate(mailingId, "owner@example.test", "recipient@example.test");
        var result = service.Validate(token);

        Assert.True(result.Ok);
        Assert.Equal("inbound_reply", result.Payload?.Purpose);
        Assert.Equal(mailingId, result.Payload?.MailingId);
        Assert.Equal("owner@example.test", result.Payload?.ClientId);
        Assert.Equal("recipient@example.test", result.Payload?.RecipientEmail);
    }

    [Fact]
    public void Reply_token_rejects_tampered_token()
    {
        var service = CreateService();
        var token = service.Generate(Guid.NewGuid(), "owner@example.test", "recipient@example.test");
        var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        var result = service.Validate(tampered);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Reply_token_does_not_accept_unsubscribe_token()
    {
        var replyService = CreateService();
        var unsubscribeService = new SignedUnsubscribeTokenService(new EmailNormalizer(), new UnsubscribeTokenOptions("reply-test-secret", TimeSpan.FromDays(1)));
        var unsubscribeToken = unsubscribeService.Generate(Guid.NewGuid(), "recipient@example.test");

        var result = replyService.Validate(unsubscribeToken);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Reply_to_address_contains_token_for_fake_provider_matching()
    {
        var service = CreateService();
        var token = service.Generate(Guid.NewGuid(), "owner@example.test", "recipient@example.test");

        var address = service.BuildReplyToAddress(token);

        Assert.StartsWith("reply+", address);
        Assert.EndsWith("@reply.test", address);
        Assert.Contains(token, address);
    }

    private static SignedInboundReplyTokenService CreateService() => new(
        new EmailNormalizer(),
        new InboundReplyTokenOptions("reply-test-secret", "reply.test", TimeSpan.FromDays(1)));
}

using Xunit;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Tests;

public sealed class UnsubscribeServicesTests
{
    [Fact]
    public void Token_service_generates_and_validates_payload()
    {
        var service = CreateService();
        var mailingId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var token = service.Generate(mailingId, "User@Example.Test");
        var result = service.Validate(token);

        Assert.True(result.Ok);
        Assert.Equal(mailingId, result.Payload?.MailingId);
        Assert.Equal("user@example.test", result.Payload?.Email);
        Assert.Equal(service.BuildRecipientKey(mailingId, "user@example.test"), result.Payload?.RecipientKey);
    }

    [Fact]
    public void Token_service_rejects_tampered_token()
    {
        var service = CreateService();
        var token = service.Generate(Guid.NewGuid(), "user@example.test");
        var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        var result = service.Validate(tampered);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Token_service_rejects_expired_token()
    {
        var service = new SignedUnsubscribeTokenService(new EmailNormalizer(), new UnsubscribeTokenOptions("test-secret", TimeSpan.FromSeconds(-1)));
        var token = service.Generate(Guid.NewGuid(), "user@example.test");

        var result = service.Validate(token);

        Assert.False(result.Ok);
        Assert.Equal("expired", result.Error);
    }

    [Fact]
    public void Recipient_key_does_not_depend_on_recipient_id()
    {
        var service = CreateService();
        var mailingId = Guid.NewGuid();

        var first = service.BuildRecipientKey(mailingId, "USER@example.test");
        var second = service.BuildRecipientKey(mailingId, "user@example.test");

        Assert.Equal(first, second);
    }

    private static SignedUnsubscribeTokenService CreateService() => new(
        new EmailNormalizer(),
        new UnsubscribeTokenOptions("test-secret", TimeSpan.FromDays(1)));
}

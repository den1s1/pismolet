using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class DomainDefaultsTests
{
    [Fact]
    public void Client_profile_defaults_match_sprint_zero_requirements()
    {
        var profile = ClientProfile.NewClientDefault();

        Assert.Equal("active", profile.Status);
        Assert.Equal(1000, profile.DailySendLimit);
        Assert.Equal(10000, profile.TotalSendLimit);
        Assert.True(profile.PremoderationRequired);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 0.90)]
    [InlineData(499, 0.90)]
    [InlineData(500, 0.80)]
    [InlineData(999, 0.80)]
    [InlineData(1000, 0.70)]
    [InlineData(1500, 0.70)]
    public void Public_mailing_tariff_matches_published_pricing(int acceptedRecipients, decimal expectedUnitPrice)
    {
        Assert.Equal(expectedUnitPrice, MailingTariff.PricePerRecipientFor(acceptedRecipients));
    }

    [Theory]
    [InlineData(299, 299)]
    [InlineData(300, 270)]
    [InlineData(500, 400)]
    [InlineData(1000, 700)]
    public void Payment_uses_public_mailing_tariff_for_total_amount(int acceptedRecipients, decimal expectedTotal)
    {
        var payment = Payment.Create(Guid.NewGuid(), "owner@example.test", acceptedRecipients, 0, PriceSettings.DefaultRub());

        Assert.Equal(MailingTariff.PricePerRecipientFor(acceptedRecipients), payment.PricePerRecipient);
        Assert.Equal(expectedTotal, payment.TotalAmount);
        Assert.Equal("RUB", payment.Currency);
    }

    [Fact]
    public void Send_event_tracking_token_is_stable_for_mailing_and_recipient()
    {
        var mailingId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var first = SendEvent.BuildTrackingToken(mailingId, "USER@Example.COM");
        var second = SendEvent.BuildTrackingToken(mailingId, "user@example.com");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("@", first);
    }
}

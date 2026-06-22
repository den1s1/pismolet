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

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
}

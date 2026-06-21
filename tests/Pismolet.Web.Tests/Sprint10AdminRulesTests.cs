using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class Sprint10AdminRulesTests
{
    [Fact]
    public void Blocked_mailing_cannot_start_sending()
    {
        Assert.False(MailingStatus.Blocked.CanStartSending());
        Assert.Equal("blocked", MailingStatus.Blocked.ToCode());
        Assert.Equal(MailingStatus.Blocked, MailingStatusLabels.FromRu("Заблокировано администратором"));
    }

    [Fact]
    public void Client_profile_block_and_unblock_use_single_status_source()
    {
        var profile = ClientProfile.NewClientDefault();

        var blocked = profile.Block("admin@example.test", "risk");
        Assert.True(blocked.IsBlocked);
        Assert.Equal(ClientStatuses.Blocked, blocked.Status);
        Assert.Equal("admin@example.test", blocked.BlockedByAdminEmail);

        var unblocked = blocked.Unblock("admin@example.test");
        Assert.False(unblocked.IsBlocked);
        Assert.Equal(ClientStatuses.Active, unblocked.Status);
        Assert.Equal("admin@example.test", unblocked.UnblockedByAdminEmail);
    }

    [Fact]
    public void Client_profile_limit_and_premoderation_changes_keep_admin_metadata()
    {
        var profile = ClientProfile.NewClientDefault()
            .WithDailyLimit(500, "admin@example.test")
            .WithPremoderation(false, "admin@example.test");

        Assert.Equal(500, profile.DailySendLimit);
        Assert.False(profile.PremoderationRequired);
        Assert.Equal("admin@example.test", profile.LimitChangedByAdminEmail);
        Assert.Equal("admin@example.test", profile.PremoderationChangedByAdminEmail);
    }

    [Fact]
    public void Admin_mvp_settings_validate_price_limits_and_retention()
    {
        var settings = AdminMvpSettings.Default with
        {
            PricePerRecipient = 2.5m,
            DefaultDailySendLimit = 200,
            ReplyBodyRetentionDays = 7
        };

        settings.EnsureValid();

        Assert.Throws<ArgumentOutOfRangeException>(() => (settings with { PricePerRecipient = -1 }).EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() => (settings with { ReplyBodyRetentionDays = 0 }).EnsureValid());
    }
}

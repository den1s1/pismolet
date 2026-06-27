using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.DependencyInjection;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailWarmupConfigurationTests
{
    [Fact]
    public void Service_registration_reads_domain_warmup_limits_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["MailWarmup:SettingsPath"] = TestSettingsPath(),
                ["MailWarmup:MaxPerMinute"] = "10",
                ["MailWarmup:MaxPerHour"] = "100",
                ["MailWarmup:MaxPerDay"] = "1000",
                ["MailWarmup:MinSecondsBetweenSends"] = "30",
                ["MailWarmup:DomainLimits:Gmail.com:MaxPerMinute"] = "2",
                ["MailWarmup:DomainLimits:Gmail.com:MaxPerHour"] = "20",
                ["MailWarmup:DomainLimits:Gmail.com:MaxPerDay"] = "200",
                ["MailWarmup:DomainLimits:Gmail.com:MinSecondsBetweenSends"] = "300",
                ["MailWarmup:DomainLimits:yandex.ru:MaxPerDay"] = "50"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddPismoletWebServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MailWarmupLimitOptions>();
        var gmail = options.GetDomainLimit("gmail.com");
        var yandex = options.GetDomainLimit("yandex.ru");

        Assert.Equal(10, options.MaxPerMinute);
        Assert.Equal(100, options.MaxPerHour);
        Assert.Equal(1000, options.MaxPerDay);
        Assert.Equal(30, options.MinSecondsBetweenSends);
        Assert.Equal(2, gmail.MaxPerMinute);
        Assert.Equal(20, gmail.MaxPerHour);
        Assert.Equal(200, gmail.MaxPerDay);
        Assert.Equal(300, gmail.MinSecondsBetweenSends);
        Assert.Equal(10, yandex.MaxPerMinute);
        Assert.Equal(100, yandex.MaxPerHour);
        Assert.Equal(50, yandex.MaxPerDay);
        Assert.Equal(30, yandex.MinSecondsBetweenSends);
    }

    [Fact]
    public void Service_registration_accepts_env_safe_domain_limit_keys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["MailWarmup:SettingsPath"] = TestSettingsPath(),
                ["MailWarmup:MaxPerMinute"] = "10",
                ["MailWarmup:MaxPerHour"] = "100",
                ["MailWarmup:MaxPerDay"] = "1000",
                ["MailWarmup:MinSecondsBetweenSends"] = "30",
                ["MailWarmup:DomainLimits:gmail_com:MaxPerMinute"] = "1",
                ["MailWarmup:DomainLimits:mail_ru:MinSecondsBetweenSends"] = "300"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddPismoletWebServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MailWarmupLimitOptions>();
        var gmail = options.GetDomainLimit("gmail.com");
        var mailRu = options.GetDomainLimit("mail.ru");

        Assert.Equal(1, gmail.MaxPerMinute);
        Assert.Equal(100, gmail.MaxPerHour);
        Assert.Equal(1000, gmail.MaxPerDay);
        Assert.Equal(30, gmail.MinSecondsBetweenSends);
        Assert.Equal(10, mailRu.MaxPerMinute);
        Assert.Equal(100, mailRu.MaxPerHour);
        Assert.Equal(1000, mailRu.MaxPerDay);
        Assert.Equal(300, mailRu.MinSecondsBetweenSends);
    }

    [Fact]
    public void Send_gate_uses_saved_runtime_warmup_settings_without_rebuilding_provider()
    {
        var now = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["MailWarmup:SettingsPath"] = TestSettingsPath(),
                ["MailWarmup:MaxPerMinute"] = "100",
                ["MailWarmup:MaxPerHour"] = "100",
                ["MailWarmup:MaxPerDay"] = "100",
                ["MailWarmup:MinSecondsBetweenSends"] = "0"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPismoletWebServices(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sendEvents = scope.ServiceProvider.GetRequiredService<ISendEventRepository>();
        sendEvents.Save(AcceptedEvent("owner@example.test", "sent@example.test", now.AddSeconds(-10)));
        var gate = scope.ServiceProvider.GetRequiredService<IMailWarmupSendGate>();
        var settings = scope.ServiceProvider.GetRequiredService<IMailWarmupRuntimeSettingsRepository>();

        var before = gate.Evaluate("owner@example.test", "target@example.test", now);
        Assert.True(before.IsAllowed);

        var saved = settings.Save(new MailWarmupRuntimeSettings(
            MaxPerMinute: 1,
            MaxPerHour: 100,
            MaxPerDay: 100,
            MinSecondsBetweenSends: 0));
        Assert.True(saved.Ok, saved.Error);

        var after = gate.Evaluate("owner@example.test", "target@example.test", now);

        Assert.False(after.IsAllowed);
        Assert.Equal("global_minute_limit", after.Reason);
    }

    [Fact]
    public void Invalid_runtime_warmup_settings_keep_previous_effective_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["MailWarmup:SettingsPath"] = TestSettingsPath(),
                ["MailWarmup:MaxPerMinute"] = "10",
                ["MailWarmup:MaxPerHour"] = "100",
                ["MailWarmup:MaxPerDay"] = "1000",
                ["MailWarmup:MinSecondsBetweenSends"] = "0"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPismoletWebServices(configuration);

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IMailWarmupRuntimeSettingsRepository>();
        var optionsProvider = provider.GetRequiredService<IMailWarmupLimitOptionsProvider>();

        var valid = settings.Save(new MailWarmupRuntimeSettings(2, 10, 100, 0));
        var invalid = settings.Save(new MailWarmupRuntimeSettings(20, 10, 100, 0));

        Assert.True(valid.Ok, valid.Error);
        Assert.False(invalid.Ok);
        var current = optionsProvider.GetCurrent();
        Assert.Equal(2, current.MaxPerMinute);
        Assert.Equal(10, current.MaxPerHour);
        Assert.Equal(100, current.MaxPerDay);
    }

    private static string TestSettingsPath() => Path.Combine(
        Path.GetTempPath(),
        $"pismolet-warmup-config-test-{Guid.NewGuid():N}.json");

    private static SendEvent AcceptedEvent(string ownerEmail, string recipientEmail, DateTimeOffset acceptedAt) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        ownerEmail,
        recipientEmail,
        SendEventStatus.Accepted,
        SendSkipReason.None,
        SendEvent.FakeProvider,
        $"provider-{Guid.NewGuid():N}",
        1,
        null,
        null,
        acceptedAt.AddMinutes(-1),
        acceptedAt,
        AcceptedAt: acceptedAt);
}

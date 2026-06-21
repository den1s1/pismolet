using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.DependencyInjection;
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
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.DependencyInjection;

namespace Pismolet.Web.Tests;

public sealed class RobokassaConfigurationTests
{
    [Fact]
    public void Service_registration_reads_robokassa_options_from_env_style_keys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Robokassa__MerchantLogin"] = "pismolet",
                ["Robokassa__Password1"] = "test-password-1",
                ["Robokassa__Password2"] = "test-password-2",
                ["Robokassa__IsTest"] = "true",
                ["Robokassa__PaymentUrl"] = "https://auth.robokassa.ru/Merchant/Index.aspx"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddPismoletWebServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<RobokassaPaymentOptions>();

        Assert.Equal("pismolet", options.MerchantLogin);
        Assert.Equal("test-password-1", options.Password1);
        Assert.Equal("test-password-2", options.Password2);
        Assert.True(options.IsTest);
        Assert.Equal("https://auth.robokassa.ru/Merchant/Index.aspx", options.PaymentUrl);
    }
}

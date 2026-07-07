using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.DependencyInjection;

public static class ProdamusServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletProdamusPayment(this IServiceCollection services, IConfiguration configuration)
    {
        var paymentPageUrl = configuration["Prodamus:PaymentPageUrl"] ?? ProdamusOptions.PublicPayformPaymentPageUrl;
        var checkPhrase = configuration["Prodamus:CallbackCheckPhrase"] ?? string.Empty;
        var serviceName = configuration["Prodamus:ServiceName"] ?? ProdamusOptions.DefaultServiceName;
        var required = bool.TryParse(configuration["Prodamus:CallbackCheckRequired"], out var parsedRequired) ? parsedRequired : true;
        var isTest = bool.TryParse(configuration["Prodamus:IsTest"], out var parsedTest) && parsedTest;

        services.AddSingleton(new ProdamusOptions(
            PaymentPageUrl: paymentPageUrl.Trim(),
            CallbackCheckValue: checkPhrase,
            CallbackCheckRequired: required,
            ServiceName: string.IsNullOrWhiteSpace(serviceName) ? ProdamusOptions.DefaultServiceName : serviceName.Trim(),
            IsTest: isTest));
        services.AddScoped<IPaymentProvider, ProdamusPaymentProvider>();
        return services;
    }
}

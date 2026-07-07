using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.DependencyInjection;

public static class ProdamusServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletProdamusPayment(this IServiceCollection services, IConfiguration configuration)
    {
        var paymentPageUrl = configuration["Prodamus:PaymentPageUrl"]
            ?? configuration["Prodamus__PaymentPageUrl"]
            ?? ProdamusOptions.DefaultPaymentPageUrl;
        var paymentPageSignatureKey = configuration["Prodamus:PaymentPageSignatureKey"]
            ?? configuration["Prodamus__PaymentPageSignatureKey"]
            ?? string.Empty;
        var sysCode = configuration["Prodamus:SysCode"]
            ?? configuration["Prodamus__SysCode"]
            ?? string.Empty;
        var checkPhrase = configuration["Prodamus:CallbackCheckPhrase"]
            ?? configuration["Prodamus__CallbackCheckPhrase"]
            ?? paymentPageSignatureKey;
        var serviceName = configuration["Prodamus:ServiceName"]
            ?? configuration["Prodamus__ServiceName"]
            ?? ProdamusOptions.DefaultServiceName;
        var required = bool.TryParse(configuration["Prodamus:CallbackCheckRequired"] ?? configuration["Prodamus__CallbackCheckRequired"], out var parsedRequired) ? parsedRequired : true;
        var isTest = bool.TryParse(configuration["Prodamus:IsTest"] ?? configuration["Prodamus__IsTest"], out var parsedTest) && parsedTest;

        services.AddSingleton(new ProdamusOptions(
            PaymentPageUrl: NormalizePaymentPageUrl(paymentPageUrl),
            PaymentPageSignatureKey: paymentPageSignatureKey.Trim(),
            SysCode: sysCode.Trim(),
            CallbackCheckValue: checkPhrase.Trim(),
            CallbackCheckRequired: required,
            ServiceName: string.IsNullOrWhiteSpace(serviceName) ? ProdamusOptions.DefaultServiceName : serviceName.Trim(),
            IsTest: isTest));
        services.AddScoped<IPaymentProvider, ProdamusPaymentProvider>();
        return services;
    }

    private static string NormalizePaymentPageUrl(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
    }
}

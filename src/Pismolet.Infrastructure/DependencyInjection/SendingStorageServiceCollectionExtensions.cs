using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Infrastructure.Persistence;

namespace Pismolet.Web.Infrastructure.DependencyInjection;

public static class SendingStorageServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletEfSendingStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? configuration["Pismolet:Persistence"] ?? "Postgres";
        if (!provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ISendEventRepository, EfSendEventRepository>();
        }

        return services;
    }
}

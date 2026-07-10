using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pismolet.Web.Infrastructure.Postmaster;

public static class MailruPostmasterPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddMailruPostmasterPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"]
            ?? configuration["Pismolet:Persistence"]
            ?? "Postgres";
        if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMailruPostmasterDashboardReader, EmptyMailruPostmasterDashboardReader>();
            return services;
        }

        var connectionString = configuration.GetConnectionString("PismoletDb")
            ?? configuration.GetConnectionString("Pismolet")
            ?? configuration["PISMOLET_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Для хранилища Mail.ru Postmaster задайте строку подключения PismoletDb.");
        }

        services.AddDbContext<MailruPostmasterDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IMailruPostmasterStorage, EfMailruPostmasterStorage>();
        services.AddScoped<IMailruPostmasterDashboardReader, EfMailruPostmasterDashboardReader>();
        services.AddSingleton(MailruPostmasterSyncOptions.Read(configuration));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IMailruPostmasterSynchronizer, MailruPostmasterSynchronizer>();
        services.AddHostedService<MailruPostmasterSyncHostedService>();
        return services;
    }
}

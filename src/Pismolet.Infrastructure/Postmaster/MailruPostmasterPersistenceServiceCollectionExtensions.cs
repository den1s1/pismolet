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
        services.AddSingleton(MailruPostmasterManualSyncOptions.Read(configuration));
        services.AddSingleton(MailruPostmasterMailingMetricsOptions.Read(configuration));

        var provider = configuration["Persistence:Provider"]
            ?? configuration["Pismolet:Persistence"]
            ?? "Postgres";
        if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMailruPostmasterDashboardReader, EmptyMailruPostmasterDashboardReader>();
            services.AddSingleton<IMailruPostmasterMailingMetricsReader, EmptyMailruPostmasterMailingMetricsReader>();
            services.AddSingleton<IMailruPostmasterSyncRunJournal, InMemoryMailruPostmasterSyncRunJournal>();
            services.AddSingleton<IMailruPostmasterManualSyncService, DisabledMailruPostmasterManualSyncService>();
            services.AddSingleton<IMailruPostmasterMailingCandidateReader, EmptyMailruPostmasterMailingCandidateReader>();
            services.AddSingleton<IMailruPostmasterMailingStatisticsSynchronizer, DisabledMailruPostmasterMailingStatisticsSynchronizer>();
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
        services.AddScoped<IMailruPostmasterMailingStorage, EfMailruPostmasterMailingStorage>();
        services.AddScoped<IMailruPostmasterMailingMetricsReader, EfMailruPostmasterMailingMetricsReader>();
        services.AddScoped<IMailruPostmasterMailingCandidateReader, EfMailruPostmasterMailingCandidateReader>();
        services.AddScoped<IMailruPostmasterMailingStatisticsSynchronizer, MailruPostmasterMailingStatisticsSynchronizer>();
        services.AddScoped<IMailruPostmasterDashboardReader, EfMailruPostmasterDashboardReader>();
        services.AddSingleton<IMailruPostmasterSyncRunJournal, EfMailruPostmasterSyncRunJournal>();
        services.AddSingleton(MailruPostmasterSyncOptions.Read(configuration));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<MailruPostmasterSynchronizer>();
        services.AddSingleton<IMailruPostmasterSynchronizer, MailruPostmasterCompositeSynchronizer>();
        services.AddSingleton<IMailruPostmasterSyncExecutor, MailruPostmasterSyncExecutor>();
        services.AddSingleton<IMailruPostmasterManualSyncService, MailruPostmasterManualSyncService>();
        services.AddHostedService<MailruPostmasterJournaledSyncHostedService>();
        return services;
    }
}

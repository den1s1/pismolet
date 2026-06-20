using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.Mail;

namespace Pismolet.Web.Infrastructure.DependencyInjection;

public static class SendingQueueServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletHangfireSendingQueue(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? configuration["Pismolet:Persistence"] ?? "Postgres";
        var queueProvider = configuration["Sending:Queue"] ?? "Hangfire";
        if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase) || queueProvider.Equals("Inline", StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        var connectionString = configuration.GetConnectionString("PismoletDb") ?? configuration.GetConnectionString("Pismolet");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Для Hangfire задайте ConnectionStrings:PismoletDb.");
        }

        services.AddPismoletHangfireQueue(configuration, connectionString);
        services.AddScoped<MailingSendJob>();
        services.AddScoped<ReplyForwardJob>();
        services.AddScoped<ReplyCleanupJob>();
        services.AddSingleton<IBackgroundMailingSendQueue, HangfireMailingSendQueue>();
        services.AddSingleton<IBackgroundReplyQueue, HangfireMailingSendQueue>();
        return services;
    }
}

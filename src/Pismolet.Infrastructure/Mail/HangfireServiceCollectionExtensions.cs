using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pismolet.Web.Infrastructure.Mail;

public static class HangfireServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletHangfireQueue(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        var schemaName = configuration["Hangfire:SchemaName"];
        schemaName = string.IsNullOrWhiteSpace(schemaName) ? "hangfire" : schemaName.Trim();

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(connectionString, new PostgreSqlStorageOptions
            {
                SchemaName = schemaName,
                PrepareSchemaIfNecessary = true
            }));

        services.AddHangfireServer(options =>
        {
            options.ServerName = configuration["Hangfire:ServerName"] ?? $"pismolet-{Environment.MachineName}";
            options.WorkerCount = ReadWorkerCount(configuration);
            options.Queues = new[] { "default" };
        });

        return services;
    }

    private static int ReadWorkerCount(IConfiguration configuration)
    {
        var raw = configuration["Hangfire:WorkerCount"];
        return int.TryParse(raw, out var value) ? Math.Clamp(value, 1, 20) : 1;
    }
}

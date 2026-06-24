using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Infrastructure.Persistence;

namespace Pismolet.Web.Infrastructure.DependencyInjection;

public static class LegalEvidenceServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletLegalEvidenceStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? configuration["Pismolet:Persistence"] ?? "Postgres";
        if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        var connectionString = ReadConnectionString(configuration);
        services.RemoveAll<ILegalEvidenceRepository>();
        services.AddScoped<ILegalEvidenceRepository, EfLegalEvidenceRepository>();

        if (!services.Any(x => x.ServiceType == typeof(LegalEvidenceDbContext)))
        {
            services.AddDbContext<LegalEvidenceDbContext>(options => options.UseNpgsql(connectionString));
        }

        return services;
    }

    public static void MigrateLegalEvidenceDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<LegalEvidenceDbContext>();
        if (db is null)
        {
            return;
        }

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS legal_document_versions (
                id uuid PRIMARY KEY,
                document_key varchar(120) NOT NULL,
                version varchar(80) NOT NULL,
                text_hash varchar(64) NOT NULL,
                text text NOT NULL,
                url varchar(512) NULL,
                is_active boolean NOT NULL,
                created_at timestamp with time zone NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_legal_document_versions_key_version
                ON legal_document_versions (document_key, version);

            CREATE INDEX IF NOT EXISTS ix_legal_document_versions_key_active
                ON legal_document_versions (document_key, is_active);

            CREATE TABLE IF NOT EXISTS legal_events (
                id uuid PRIMARY KEY,
                event_type varchar(120) NOT NULL,
                client_id varchar(254) NOT NULL,
                user_id varchar(254) NULL,
                import_batch_id uuid NULL,
                mailing_id uuid NULL,
                document_key varchar(120) NULL,
                document_version varchar(80) NULL,
                text_hash varchar(64) NULL,
                event_text_snapshot varchar(16000) NULL,
                result varchar(80) NOT NULL,
                ip varchar(80) NULL,
                user_agent varchar(512) NULL,
                route varchar(512) NULL,
                metadata_json text NOT NULL DEFAULT '{}',
                created_at timestamp with time zone NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_legal_events_client_created_at
                ON legal_events (client_id, created_at);

            CREATE INDEX IF NOT EXISTS ix_legal_events_mailing_created_at
                ON legal_events (mailing_id, created_at);

            CREATE INDEX IF NOT EXISTS ix_legal_events_import_batch_created_at
                ON legal_events (import_batch_id, created_at);

            CREATE INDEX IF NOT EXISTS ix_legal_events_event_type
                ON legal_events (event_type);
            """);
    }

    private static string ReadConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PismoletDb")
            ?? configuration.GetConnectionString("Pismolet")
            ?? configuration["PISMOLET_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Для Postgres задайте строку подключения PismoletDb.");
        }

        return connectionString;
    }
}

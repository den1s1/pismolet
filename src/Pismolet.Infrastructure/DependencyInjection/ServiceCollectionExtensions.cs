using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Pismolet.Web.Infrastructure.Seed;

namespace Pismolet.Web.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFakeMailer, InMemoryFakeMailer>();
        services.AddSingleton<IEmailNormalizer, EmailNormalizer>();
        services.AddSingleton<IEmailSyntaxValidator, EmailSyntaxValidator>();
        services.AddSingleton<IUnsubscribeTokenService, DevUnsubscribeTokenService>();
        services.AddSingleton<IMessageRenderingService, MessageRenderingService>();

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IMailingService, MailingService>();
        services.AddScoped<IRecipientImportService, RecipientImportService>();
        services.AddScoped<IMailingDeclarationService, MailingDeclarationService>();
        services.AddScoped<IMailingMessageService, MailingMessageService>();
        services.AddScoped<DevSeedDataInitializer>();

        var provider = configuration["Persistence:Provider"]
            ?? configuration["Pismolet:Persistence"]
            ?? "Postgres";

        if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IUserRepository, InMemoryUserRepository>();
            services.AddSingleton<IMailingRepository, InMemoryMailingRepository>();
            services.AddSingleton<IGlobalSuppressionRepository, InMemoryGlobalSuppressionRepository>();
            services.AddSingleton<IAuditLogger, InMemoryAuditLogger>();
            return services;
        }

        if (!provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
            && !provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Неизвестный Persistence:Provider '{provider}'. Используйте Postgres или InMemory.");
        }

        var connectionString = configuration.GetConnectionString("PismoletDb")
            ?? configuration.GetConnectionString("Pismolet")
            ?? configuration["PISMOLET_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Для Persistence:Provider=Postgres задайте ConnectionStrings:PismoletDb или PISMOLET_CONNECTION_STRING.");
        }

        services.AddDbContext<PismoletDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IMailingRepository, EfMailingRepository>();
        services.AddScoped<IGlobalSuppressionRepository, EfGlobalSuppressionRepository>();
        services.AddScoped<IAuditLogger, EfAuditLogger>();

        return services;
    }

    public static void MigratePismoletDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<PismoletDbContext>();
        db?.Database.Migrate();
    }

    public static void SeedPismoletDevData(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<DevSeedDataInitializer>().Seed();
    }
}

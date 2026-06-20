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
        services.AddSingleton(new UnsubscribeTokenOptions(
            configuration["Unsubscribe:Secret"] ?? configuration["PISMOLET_UNSUBSCRIBE_SECRET"] ?? Environment.GetEnvironmentVariable("PISMOLET_UNSUBSCRIBE_SECRET") ?? UnsubscribeTokenOptions.DevelopmentDefault.Secret,
            TimeSpan.FromDays(ReadTokenLifetimeDays(configuration))));
        services.AddSingleton<IUnsubscribeTokenService, SignedUnsubscribeTokenService>();
        services.AddSingleton<IMessageRenderingService, MessageRenderingService>();
        services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
        services.AddSingleton<IPriceSettingsRepository, InMemoryPriceSettingsRepository>();
        services.AddSingleton<IRiskCheckRepository, InMemoryRiskCheckRepository>();
        services.AddSingleton<IModerationReviewRepository, InMemoryModerationReviewRepository>();
        services.AddSingleton<IModerationActionLogRepository, InMemoryModerationActionLogRepository>();
        services.AddSingleton<ISendEventRepository, InMemorySendEventRepository>();
        services.AddSingleton<IBackgroundMailingSendQueue, InlineMailingSendQueue>();
        services.AddSingleton(new MailingSendOptions(ReadBatchSize(configuration), ReadPublicBaseUrl(configuration)));

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IMailingService, MailingService>();
        services.AddScoped<IRecipientImportService, RecipientImportService>();
        services.AddScoped<IMailingDeclarationService, MailingDeclarationService>();
        services.AddScoped<IMailingMessageService, MailingMessageService>();
        services.AddScoped<IMailingPricingService, MailingPricingService>();
        services.AddScoped<IMailingPaymentService, MailingPaymentService>();
        services.AddScoped<IPaymentProvider, FakePaymentProvider>();
        services.AddScoped<IRiskCheckService, RiskCheckService>();
        services.AddScoped<IMailingReviewService, MailingReviewService>();
        services.AddScoped<IModerationAdminService, ModerationAdminService>();
        if (UseSmtpEmailProvider(configuration))
        {
            services.AddSingleton(SmtpEmailProviderOptions.FromConfiguration(configuration));
            services.AddScoped<IEmailProviderAdapter, SmtpEmailProviderAdapter>();
        }
        else
        {
            services.AddScoped<IEmailProviderAdapter, FakeEmailProviderAdapter>();
        }
        services.AddScoped<IMailingSendService, MailingSendService>();
        services.AddScoped<IClientSendLimitAdminService, ClientSendLimitAdminService>();
        services.AddScoped<IGlobalUnsubscribeService, GlobalUnsubscribeService>();
        services.AddScoped<DevSeedDataInitializer>();

        var provider = configuration["Persistence:Provider"] ?? configuration["Pismolet:Persistence"] ?? "Postgres";
        if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IUserRepository, InMemoryUserRepository>();
            services.AddSingleton<IMailingRepository, InMemoryMailingRepository>();
            services.AddSingleton<IGlobalSuppressionRepository, InMemoryGlobalSuppressionRepository>();
            services.AddSingleton<IAuditLogger, InMemoryAuditLogger>();
            return services;
        }

        var connectionString = configuration.GetConnectionString("PismoletDb") ?? configuration.GetConnectionString("Pismolet") ?? configuration["PISMOLET_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("PISMOLET_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Для Postgres задайте строку подключения PismoletDb.");

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

    private static int ReadBatchSize(IConfiguration configuration)
    {
        var raw = configuration["Sending:BatchSize"];
        return int.TryParse(raw, out var value) ? Math.Clamp(value, 1, 1000) : 100;
    }

    private static int ReadTokenLifetimeDays(IConfiguration configuration)
    {
        var raw = configuration["Unsubscribe:TokenLifetimeDays"];
        return int.TryParse(raw, out var value) ? Math.Clamp(value, 1, 365) : 90;
    }

    private static string ReadPublicBaseUrl(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable("PISMOLET_PUBLIC_BASE_URL")
            ?? configuration["Public:BaseUrl"]
            ?? configuration["Pismolet:PublicBaseUrl"]
            ?? "http://localhost:5080";
        var value = raw.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Для публичных ссылок задайте Public:BaseUrl или PISMOLET_PUBLIC_BASE_URL как абсолютный http/https URL.");
        }

        return value;
    }

    private static bool UseSmtpEmailProvider(IConfiguration configuration)
    {
        var provider = Environment.GetEnvironmentVariable("PISMOLET_SENDING_PROVIDER")
            ?? configuration["Sending:Provider"]
            ?? configuration["Email:Provider"]
            ?? configuration["Pismolet:MailProvider"]
            ?? "Fake";

        return provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase);
    }
}

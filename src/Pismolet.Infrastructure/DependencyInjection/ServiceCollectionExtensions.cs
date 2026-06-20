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
        services.AddSingleton(new InboundReplyTokenOptions(
            configuration["InboundReplies:Secret"] ?? configuration["PISMOLET_INBOUND_REPLY_SECRET"] ?? Environment.GetEnvironmentVariable("PISMOLET_INBOUND_REPLY_SECRET") ?? InboundReplyTokenOptions.DevelopmentDefault.Secret,
            configuration["InboundReplies:Domain"] ?? InboundReplyTokenOptions.DevelopmentDefault.InboundDomain,
            TimeSpan.FromDays(ReadInboundTokenLifetimeDays(configuration))));
        services.AddSingleton(new InboundReplyOptions(
            ReadInt(configuration, "InboundReplies:BodyRetentionDays", 14, 1, 60),
            ReadInt(configuration, "InboundReplies:MaxStoredBodyChars", 12000, 0, 16000),
            ReadInt(configuration, "InboundReplies:ForwardBatchSize", 50, 1, 500)));
        services.AddSingleton<IUnsubscribeTokenService, SignedUnsubscribeTokenService>();
        services.AddSingleton<IInboundReplyTokenService, SignedInboundReplyTokenService>();
        services.AddSingleton<IMessageRenderingService, MessageRenderingService>();
        services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
        services.AddSingleton<IPriceSettingsRepository, InMemoryPriceSettingsRepository>();
        services.AddSingleton<IRiskCheckRepository, InMemoryRiskCheckRepository>();
        services.AddSingleton<IModerationReviewRepository, InMemoryModerationReviewRepository>();
        services.AddSingleton<IModerationActionLogRepository, InMemoryModerationActionLogRepository>();
        services.AddSingleton<ISendEventRepository, InMemorySendEventRepository>();
        services.AddSingleton<IProviderWebhookEventRepository, InMemoryProviderWebhookEventRepository>();
        services.AddSingleton<IClientSuppressionRepository, InMemoryClientSuppressionRepository>();
        services.AddSingleton<IReplyEventRepository, InMemoryReplyEventRepository>();
        services.AddSingleton<IBackgroundMailingSendQueue, InlineMailingSendQueue>();
        services.AddSingleton<IBackgroundReplyQueue, InlineMailingSendQueue>();
        services.AddSingleton(new MailingSendOptions(ReadBatchSize(configuration)));

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
        services.AddScoped<IEmailProviderAdapter, FakeEmailProviderAdapter>();
        services.AddScoped<IMailingSendService, MailingSendService>();
        services.AddScoped<IClientSendLimitAdminService, ClientSendLimitAdminService>();
        services.AddScoped<IGlobalUnsubscribeService, GlobalUnsubscribeService>();
        services.AddScoped<IEmailWebhookProcessingService, EmailWebhookProcessingService>();
        services.AddScoped<IInboundReplyMatchingService, InboundReplyMatchingService>();
        services.AddScoped<IInboundReplyProcessingService, InboundReplyProcessingService>();
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
        services.AddScoped<IProviderWebhookEventRepository, EfProviderWebhookEventRepository>();
        services.AddScoped<IClientSuppressionRepository, EfClientSuppressionRepository>();
        services.AddScoped<IReplyEventRepository, EfReplyEventRepository>();
        services.AddScoped<IAuditLogger, EfAuditLogger>();
        return services;
    }

    public static void MigratePismoletDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<PismoletDbContext>();
        if (db is null) return;

        var migrations = db.Database.GetMigrations().ToArray();
        if (migrations.Length == 0)
        {
            db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS \"__EFMigrationsHistory\";");
            db.Database.EnsureCreated();
            return;
        }

        db.Database.Migrate();
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

    private static int ReadInboundTokenLifetimeDays(IConfiguration configuration)
    {
        var raw = configuration["InboundReplies:TokenLifetimeDays"];
        return int.TryParse(raw, out var value) ? Math.Clamp(value, 1, 365) : 180;
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        var raw = configuration[key];
        return int.TryParse(raw, out var value) ? Math.Clamp(value, min, max) : fallback;
    }
}

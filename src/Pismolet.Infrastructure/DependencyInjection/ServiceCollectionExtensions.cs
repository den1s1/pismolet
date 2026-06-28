using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Admin;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Auth;
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
        services.AddSingleton<IAdminAccessService, ConfigurationAdminAccessService>();
        services.AddSingleton<IAdminNotificationSettingsRepository>(_ => new FileAdminNotificationSettingsRepository(configuration));
        services.AddSingleton<IEmailSyntaxValidator, EmailSyntaxValidator>();
        services.AddSingleton<IAdminMvpSettingsRepository, RuntimeAdminMvpSettingsRepository>();
        services.AddSingleton<IMailWarmupRuntimeSettingsRepository>(_ => new FileMailWarmupRuntimeSettingsRepository(configuration));
        services.AddSingleton(new PublicUrlOptions(ReadPublicBaseUrl(configuration)));
        services.AddSingleton(ReadRobokassaPaymentOptions(configuration));
        services.AddSingleton(new UnsubscribeTokenOptions(configuration["Unsubscribe:Secret"] ?? configuration["PISMOLET_UNSUBSCRIBE_SECRET"] ?? Environment.GetEnvironmentVariable("PISMOLET_UNSUBSCRIBE_SECRET") ?? UnsubscribeTokenOptions.DevelopmentDefault.Secret, TimeSpan.FromDays(ReadTokenLifetimeDays(configuration))));
        services.AddSingleton(new InboundReplyTokenOptions(configuration["InboundReplies:Secret"] ?? configuration["PISMOLET_INBOUND_REPLY_SECRET"] ?? Environment.GetEnvironmentVariable("PISMOLET_INBOUND_REPLY_SECRET") ?? InboundReplyTokenOptions.DevelopmentDefault.Secret, configuration["InboundReplies:Domain"] ?? InboundReplyTokenOptions.DevelopmentDefault.InboundDomain, TimeSpan.FromDays(ReadInboundTokenLifetimeDays(configuration))));
        services.AddSingleton(new InboundReplyOptions(ReadInt(configuration, "InboundReplies:BodyRetentionDays", 14, 1, 60), ReadInt(configuration, "InboundReplies:MaxStoredBodyChars", 12000, 0, 16000), ReadInt(configuration, "InboundReplies:ForwardBatchSize", 50, 1, 500)));
        services.AddSingleton(ReadMailWarmupLimitOptions(configuration));
        services.AddSingleton<IMailWarmupLimitOptionsProvider, RuntimeMailWarmupLimitOptionsProvider>();
        services.AddSingleton(ReadPostfixDeliveryLogReaderOptions(configuration));
        services.AddSingleton<IMailWarmupThrottle, MailWarmupThrottle>();
        services.AddScoped<IMailWarmupSendGate, MailWarmupSendGate>();
        services.AddSingleton<IUnsubscribeTokenService, SignedUnsubscribeTokenService>();
        services.AddSingleton<IInboundReplyTokenService, SignedInboundReplyTokenService>();
        services.AddSingleton<IMessageRenderingService, MessageRenderingService>();
        services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
        services.AddSingleton<IPriceSettingsRepository, InMemoryPriceSettingsRepository>();
        services.AddSingleton<IRiskCheckRepository, InMemoryRiskCheckRepository>();
        services.AddSingleton<IModerationReviewRepository, InMemoryModerationReviewRepository>();
        services.AddSingleton<IModerationActionLogRepository, InMemoryModerationActionLogRepository>();
        services.AddSingleton<ISendEventRepository, InMemorySendEventRepository>();
        services.AddSingleton<IClickTrackingRepository, InMemoryClickTrackingRepository>();
        services.AddSingleton<IPostfixDeliveryEventRepository, InMemoryPostfixDeliveryEventRepository>();
        services.AddSingleton<IProviderWebhookEventRepository, InMemoryProviderWebhookEventRepository>();
        services.AddSingleton<IClientSuppressionRepository, InMemoryClientSuppressionRepository>();
        services.AddSingleton<IReplyEventRepository, InMemoryReplyEventRepository>();
        services.AddSingleton<IClientReplyAliasRepository, InMemoryClientReplyAliasRepository>();
        services.AddSingleton<IOutboundReplyMessageRepository, InMemoryOutboundReplyMessageRepository>();
        services.AddSingleton<ILegalEvidenceRepository, InMemoryLegalEvidenceRepository>();
        services.AddSingleton<IBackgroundMailingSendQueue, InlineMailingSendQueue>();
        services.AddSingleton<IBackgroundReplyQueue, InlineMailingSendQueue>();
        services.AddSingleton(new MailingSendOptions(ReadBatchSize(configuration)));
        AddEmailProvider(services, configuration);

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IMailingService, MailingService>();
        services.AddScoped<IRecipientImportService, RecipientImportService>();
        services.AddScoped<IMailingDeclarationService, MailingDeclarationService>();
        services.AddScoped<IMailingMessageService, MailingMessageService>();
        services.AddScoped<IMailingPricingService, MailingPricingService>();
        services.AddScoped<MailingPaymentService>();
        services.AddScoped<IMailingPaymentService, AdminWaivedMailingPaymentService>();
        services.AddScoped<IPaymentProvider, FakeRobokassaPaymentProvider>();
        services.AddScoped<IRiskCheckService, RiskCheckService>();
        services.AddScoped<IMailingReviewService, MailingReviewService>();
        services.AddScoped<IModerationAdminService, ModerationAdminService>();
        services.AddScoped<MailingSendService>();
        services.AddScoped<IMailingSendService, AdminGuardedMailingSendService>();
        services.AddScoped<IClientSendLimitAdminService, ClientSendLimitAdminService>();
        services.AddScoped<IAdminUserRemovalService, AdminUserRemovalService>();
        services.AddScoped<IAdminNotificationService, AdminNotificationService>();
        services.AddScoped<IAdminOperationService, AdminOperationService>();
        services.AddScoped<IGlobalUnsubscribeService, GlobalUnsubscribeService>();
        services.AddScoped<IEmailWebhookProcessingService, EmailWebhookProcessingService>();
        services.AddScoped<IClientReplyAliasService, ClientReplyAliasService>();
        services.AddScoped<IInboundReplyMatchingService, AliasInboundReplyMatchingService>();
        services.AddScoped<IInboundReplyProcessingService, AliasInboundReplyProcessingService>();
        services.AddScoped<ILegalEvidenceService, LegalEvidenceService>();
        services.AddScoped<PostfixDeliveryLogIngestionService>();
        services.AddScoped<PostfixDeliveryLogReaderService>();
        services.AddScoped<DevSeedDataInitializer>();

        var provider = configuration["Persistence:Provider"] ?? configuration["Pismolet:Persistence"] ?? "Postgres";
        if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IUserRepository, InMemoryUserRepository>();
            services.AddSingleton<IMailingRepository, InMemoryMailingRepository>();
            services.AddSingleton<IGlobalSuppressionRepository, InMemoryGlobalSuppressionRepository>();
            services.AddSingleton<IAdminRecipientRepository, InMemoryAdminRecipientRepository>();
            services.AddSingleton<IAdminNotificationSettingsRepository, InMemoryAdminNotificationSettingsRepository>();
            services.AddSingleton<InMemoryAdminMailingSummaryRepository>();
            services.AddSingleton<IAdminCampaignRepository>(sp => sp.GetRequiredService<InMemoryAdminMailingSummaryRepository>());
            services.AddSingleton<IAdminPaymentRepository>(sp => sp.GetRequiredService<InMemoryAdminMailingSummaryRepository>());
            services.AddSingleton<IAuditLogger, InMemoryAuditLogger>();
            return services;
        }

        var connectionString = configuration.GetConnectionString("PismoletDb") ?? configuration.GetConnectionString("Pismolet") ?? configuration["PISMOLET_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("PISMOLET_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Для Postgres задайте строку подключения PismoletDb.");
        services.AddDbContext<PismoletDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<LegalEvidenceDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IMailingRepository, EfMailingRepository>();
        services.AddScoped<IGlobalSuppressionRepository, EfGlobalSuppressionRepository>();
        services.AddScoped<ISendEventRepository, EfSendEventRepository>();
        services.AddScoped<IClickTrackingRepository, EfClickTrackingRepository>();
        services.AddScoped<IPostfixDeliveryEventRepository, EfPostfixDeliveryEventRepository>();
        services.AddScoped<IAdminRecipientRepository, EfAdminRecipientRepository>();
        services.AddScoped<EfAdminMailingSummaryRepository>();
        services.AddScoped<IAdminCampaignRepository>(sp => sp.GetRequiredService<EfAdminMailingSummaryRepository>());
        services.AddScoped<IAdminPaymentRepository>(sp => sp.GetRequiredService<EfAdminMailingSummaryRepository>());
        services.AddScoped<IProviderWebhookEventRepository, EfProviderWebhookEventRepository>();
        services.AddScoped<IClientSuppressionRepository, EfClientSuppressionRepository>();
        services.AddScoped<IReplyEventRepository, EfReplyEventRepository>();
        services.AddScoped<IClientReplyAliasRepository, EfClientReplyAliasRepository>();
        services.AddScoped<IOutboundReplyMessageRepository, EfOutboundReplyMessageRepository>();
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

    public static void EnsurePismoletRuntimeSchema(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<PismoletDbContext>();
        if (db is null) return;

        db.Database.ExecuteSqlRaw("ALTER TABLE audit_records ADD COLUMN IF NOT EXISTS \"At\" timestamp with time zone NOT NULL DEFAULT now();");
        db.Database.ExecuteSqlRaw("ALTER TABLE audit_records ADD COLUMN IF NOT EXISTS \"CreatedAt\" timestamp with time zone DEFAULT now();");
        db.Database.ExecuteSqlRaw("UPDATE audit_records SET \"CreatedAt\" = now() WHERE \"CreatedAt\" IS NULL;");
        db.Database.ExecuteSqlRaw("ALTER TABLE audit_records ALTER COLUMN \"CreatedAt\" SET DEFAULT now();");
        db.Database.ExecuteSqlRaw("ALTER TABLE users ADD COLUMN IF NOT EXISTS \"Phone\" character varying(40) NOT NULL DEFAULT '';");
    }

    public static void SeedPismoletDevData(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<DevSeedDataInitializer>().Seed();
    }

    private static void AddEmailProvider(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["MailProvider"] ?? configuration["Mail:Provider"] ?? configuration["Email:Provider"] ?? configuration["Sending:MailProvider"] ?? "FakeMailer";
        if (provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(ReadSmtpOptions(configuration));
            services.AddScoped<IEmailProviderAdapter, SmtpEmailProviderAdapter>();
            return;
        }
        services.AddScoped<IEmailProviderAdapter, PublicUrlFakeEmailProviderAdapter>();
    }

    private static RobokassaPaymentOptions ReadRobokassaPaymentOptions(IConfiguration configuration)
    {
        var fallback = RobokassaPaymentOptions.DevelopmentDefault;
        return new RobokassaPaymentOptions(
            MerchantLogin: configuration["Robokassa:MerchantLogin"] ?? configuration["Robokassa__MerchantLogin"] ?? fallback.MerchantLogin,
            Password1: configuration["Robokassa:Password1"] ?? configuration["Robokassa__Password1"] ?? fallback.Password1,
            Password2: configuration["Robokassa:Password2"] ?? configuration["Robokassa__Password2"] ?? fallback.Password2,
            IsTest: ReadBool(configuration, "Robokassa:IsTest", fallback.IsTest),
            PaymentUrl: configuration["Robokassa:PaymentUrl"] ?? configuration["Robokassa__PaymentUrl"] ?? fallback.PaymentUrl);
    }

    private static SmtpEmailProviderOptions ReadSmtpOptions(IConfiguration configuration)
    {
        var host = Required(configuration, "Smtp:Host", "Smtp__Host");
        var port = ReadInt(configuration, "Smtp:Port", 587, 1, 65535);
        var username = configuration["Smtp:Username"] ?? string.Empty;
        var password = configuration["Smtp:Password"] ?? string.Empty;
        var fromEmail = configuration["Smtp:FromEmail"] ?? username;
        var fromName = configuration["Smtp:FromName"] ?? "Письмолёт";
        var secureSocketOptions = configuration["Smtp:SecureSocketOptions"] ?? configuration["Smtp:Security"] ?? (port == 465 ? "SslOnConnect" : "StartTlsWhenAvailable");
        var timeoutSeconds = ReadInt(configuration, "Smtp:TimeoutSeconds", 30, 1, 300);
        if (string.IsNullOrWhiteSpace(fromEmail)) throw new InvalidOperationException("Для SMTP задайте Smtp:FromEmail или Smtp:Username.");
        return new SmtpEmailProviderOptions(host.Trim(), port, username.Trim(), password, fromEmail.Trim(), string.IsNullOrWhiteSpace(fromName) ? "Письмолёт" : fromName.Trim(), secureSocketOptions.Trim(), timeoutSeconds);
    }

    private static MailWarmupLimitOptions ReadMailWarmupLimitOptions(IConfiguration configuration)
    {
        var settings = FileMailWarmupRuntimeSettingsRepository.ReadCurrent(configuration);
        return new MailWarmupLimitOptions(
            MaxPerMinute: settings.MaxPerMinute,
            MaxPerHour: settings.MaxPerHour,
            MaxPerDay: settings.MaxPerDay,
            MinSecondsBetweenSends: settings.MinSecondsBetweenSends,
            DomainLimits: ReadMailWarmupDomainLimits(configuration));
    }

    private static IReadOnlyDictionary<string, DomainMailWarmupLimitOptions>? ReadMailWarmupDomainLimits(IConfiguration configuration)
    {
        var section = configuration.GetSection("MailWarmup:DomainLimits");
        var limits = new Dictionary<string, DomainMailWarmupLimitOptions>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in section.GetChildren())
        {
            var domain = NormalizeWarmupDomainKey(child.Key);
            if (string.IsNullOrWhiteSpace(domain))
            {
                continue;
            }

            var limit = new DomainMailWarmupLimitOptions(
                MaxPerMinute: ReadNullableInt(child, "MaxPerMinute", 0, 100000),
                MaxPerHour: ReadNullableInt(child, "MaxPerHour", 0, 100000),
                MaxPerDay: ReadNullableInt(child, "MaxPerDay", 0, 100000),
                MinSecondsBetweenSends: ReadNullableInt(child, "MinSecondsBetweenSends", 0, 86400));

            limits[domain] = limit;
        }

        return limits.Count == 0 ? null : limits;
    }

    private static PostfixDeliveryLogReaderOptions ReadPostfixDeliveryLogReaderOptions(IConfiguration configuration)
    {
        var logPath = configuration["PostfixDelivery:LogPath"]
            ?? configuration["PostfixDelivery__LogPath"]
            ?? configuration["PISMOLET_POSTFIX_DELIVERY_LOG_PATH"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_POSTFIX_DELIVERY_LOG_PATH")
            ?? PostfixDeliveryLogReaderOptions.ProductionDefault.LogPath;
        var cursorPath = configuration["PostfixDelivery:CursorPath"]
            ?? configuration["PostfixDelivery__CursorPath"]
            ?? configuration["PISMOLET_POSTFIX_DELIVERY_CURSOR_PATH"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_POSTFIX_DELIVERY_CURSOR_PATH")
            ?? PostfixDeliveryLogReaderOptions.ProductionDefault.CursorPath;
        var year = ReadInt(configuration, "PostfixDelivery:Year", DateTimeOffset.UtcNow.Year, 2000, 2100);
        var utcOffsetMinutes = ReadInt(configuration, "PostfixDelivery:UtcOffsetMinutes", 0, -1440, 1440);
        var startAtEndWhenCursorMissing = ReadBool(configuration, "PostfixDelivery:StartAtEndWhenCursorMissing", true);
        return new PostfixDeliveryLogReaderOptions(logPath, cursorPath, year, TimeSpan.FromMinutes(utcOffsetMinutes), startAtEndWhenCursorMissing);
    }

    private static string NormalizeWarmupDomainKey(string key) => key.Replace("__", ".", StringComparison.Ordinal).Replace('_', '.').Trim().ToLowerInvariant();

    private static int ReadBatchSize(IConfiguration configuration) => ReadInt(configuration, "Sending:BatchSize", 100, 1, 5000);

    private static int ReadTokenLifetimeDays(IConfiguration configuration) => ReadInt(configuration, "Unsubscribe:TokenLifetimeDays", 3650, 1, 36500);

    private static int ReadInboundTokenLifetimeDays(IConfiguration configuration) => ReadInt(configuration, "InboundReplies:TokenLifetimeDays", 3650, 1, 36500);

    private static int ReadInt(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        var value = configuration[key] ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)];
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        var value = configuration[key] ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)];
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int? ReadNullableInt(IConfigurationSection section, string key, int min, int max) => int.TryParse(section[key], out var parsed)
        ? Math.Clamp(parsed, min, max)
        : null;

    private static string Required(IConfiguration configuration, string key, string envKey)
    {
        var value = configuration[key] ?? configuration[envKey] ?? Environment.GetEnvironmentVariable(envKey);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Не задан обязательный параметр {key}.");
        return value;
    }

    private static string ReadPublicBaseUrl(IConfiguration configuration)
    {
        var raw = configuration["PublicBaseUrl"]
            ?? configuration["App:PublicBaseUrl"]
            ?? configuration["Pismolet:PublicBaseUrl"]
            ?? configuration["PISMOLET_PUBLIC_BASE_URL"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_PUBLIC_BASE_URL")
            ?? PublicUrlOptions.DevelopmentDefault.PublicBaseUrl;
        return NormalizeHttpBaseUrl(raw, PublicUrlOptions.DevelopmentDefault.PublicBaseUrl);
    }

    private static string NormalizeHttpBaseUrl(string? raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return value.TrimEnd('/');
        }

        return fallback.TrimEnd('/');
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.BackgroundServices;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.DependencyInjection;
using Pismolet.Web.Infrastructure.Mail;

var builder = WebApplication.CreateBuilder(args);
var isRunningUnderTests = builder.Environment.IsEnvironment("Testing") || IsRunningUnderTests();

if (isRunningUnderTests)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Persistence:Provider"] = "InMemory",
        ["MailProvider"] = "FakeMailer"
    });
}

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminEndpoints.AdminPolicyName, policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context =>
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email) || context.Resource is not HttpContext httpContext)
            {
                return false;
            }

            return httpContext.RequestServices.GetRequiredService<IAdminAccessService>().IsAdminEmail(email);
        }));
});
builder.Services.AddPismoletWebServices(builder.Configuration);
builder.Services.AddSingleton(ReadInboundReplySpoolOptions(builder.Configuration));
builder.Services.AddSingleton<IInboundReplyMimeParser, PostfixRawMimeInboundReplyParser>();
builder.Services.AddScoped<MailingPaymentService>();
builder.Services.AddScoped<IMailingPaymentService, AdminWaivedMailingPaymentService>();
if (!isRunningUnderTests && ShouldUseSmtpConfirmation(builder.Configuration))
{
    builder.Services.AddSingleton<IFakeMailer, SmtpAccountConfirmationMailer>();
}
builder.Services.AddPismoletLegalEvidenceStorage(builder.Configuration);
builder.Services.AddPismoletEfSendingStorage(builder.Configuration);
builder.Services.AddSingleton(ReadPostfixDeliveryAutomationSettingsOptions(builder.Configuration));
builder.Services.AddSingleton<IPostfixDeliveryAutomationSettingsRepository, FilePostfixDeliveryAutomationSettingsRepository>();
if (!isRunningUnderTests)
{
    builder.Services.AddHostedService<PostfixDeliveryLogReaderHostedService>();
    builder.Services.AddHostedService<InboundReplySpoolReaderHostedService>();
}

var app = builder.Build();

if (!isRunningUnderTests)
{
    try
    {
        app.Services.MigrateLegalEvidenceDatabase();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Не удалось инициализировать хранилище legal evidence. Приложение продолжит запуск без блокировки старта.");
    }

    try
    {
        app.Services.EnsurePismoletRuntimeSchema();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Не удалось проверить runtime-схему основной БД. Приложение продолжит запуск.");
    }
}

if (app.Environment.IsDevelopment() && !isRunningUnderTests)
{
    app.Services.MigratePismoletDatabase();
    app.Services.SeedPismoletDevData();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAdminMenuVisibility();
app.UseAdminSettingsMenuLink();
app.UseUnifiedAdminSidebar();
app.UseDashboardHeroRemoval();
app.UseRecipientManagementAjax();
app.UseLegalDeclarationEvidenceCapture();
app.UseMailingLaunchEvidenceCapture();
app.UseAdminSuppressionDetail();
app.UseAdminSuppressions();
app.UseAdminCampaignDeliveryAnalytics();
app.UseAdminCampaignOpenAnalytics();
app.UseAdminCampaignClickAnalytics();
app.UseAdminDeliveryMailingDrilldown();
app.UseAdminDeliveryClientDrilldown();

app.MapHomeEndpoints();
app.MapLegalDocumentEndpoints();
app.MapLegalBaseLawfulnessEndpoints();
app.MapAccountEndpoints();
app.MapMailingCreationFlowReworkEndpoints();
app.MapMailingRichMessageFlowEndpoints();
app.MapMailingRecipientReviewOverlayEndpoints();
app.MapSimplifiedRecipientStepEndpoints();
app.MapDashboardEndpoints();
app.MapSimplifiedMessageStepEndpoints();
app.MapProfileEndpoints();
app.MapPaymentEndpoints();
app.MapCheckEndpoints();
app.MapSendEndpoints();
app.MapAdminModerationAutoLaunchEndpoints();
app.MapAdminEndpoints();
app.MapAdminUsersPageEndpoints();
app.MapAdminLegalEvidenceEndpoints();
app.MapAdminDeliveryEndpoints();
app.MapAdminPaymentEndpoints();
app.MapAdminSettingsEndpoints();
app.MapAdminPostfixDeliveryEndpoints();
app.MapAdminPostfixDeliverySettingsEndpoints();
app.MapAdminReplyEndpoints();
app.MapAdminSprint10Endpoints();
app.MapUnsubscribeEndpoints();
app.MapOpenTrackingEndpoints();
app.MapClickTrackingEndpoints();
app.MapWebhookEndpoints();
app.MapInboundReplyEndpoints();

if (app.Environment.IsDevelopment() && DevToolsEnabled(app.Configuration))
{
    app.MapDevEndpoints();
    app.MapDevWebhookEndpoints();
    app.MapDevInboundReplyEndpoints();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static bool IsRunningUnderTests() => AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
{
    var name = assembly.GetName().Name;
    return name is "testhost" || (name?.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ?? false);
});

static bool DevToolsEnabled(IConfiguration configuration) => bool.TryParse(
    configuration["DevTools:Enabled"]
    ?? configuration["DevTools__Enabled"]
    ?? Environment.GetEnvironmentVariable("PISMOLET_DEV_TOOLS_ENABLED"),
    out var enabled) && enabled;

static bool ShouldUseSmtpConfirmation(IConfiguration configuration)
{
    var provider = configuration["MailProvider"]
        ?? configuration["Mail:Provider"]
        ?? configuration["Email:Provider"]
        ?? configuration["Sending:MailProvider"];

    if (!string.IsNullOrWhiteSpace(provider))
    {
        return provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase);
    }

    return !string.IsNullOrWhiteSpace(configuration["Smtp:Host"]
        ?? configuration["Smtp__Host"]
        ?? Environment.GetEnvironmentVariable("Smtp__Host"));
}

static InboundReplySpoolOptions ReadInboundReplySpoolOptions(IConfiguration configuration)
{
    var fallback = InboundReplySpoolOptions.DevelopmentDefault;
    var spoolPath = configuration["InboundReplies:SpoolPath"]
        ?? configuration["InboundReplies__SpoolPath"]
        ?? fallback.SpoolPath;

    return new InboundReplySpoolOptions(
        ReadBool(configuration, "InboundReplies:Enabled", fallback.Enabled),
        spoolPath.Trim(),
        ReadInt(configuration, "InboundReplies:PollIntervalSeconds", fallback.PollIntervalSeconds, InboundReplySpoolOptions.MinPollIntervalSeconds, InboundReplySpoolOptions.MaxPollIntervalSeconds),
        ReadLong(configuration, "InboundReplies:MaxMessageBytes", fallback.MaxMessageBytes, InboundReplySpoolOptions.MinMessageBytes, InboundReplySpoolOptions.MaxAllowedMessageBytes),
        ReadInt(configuration, "InboundReplies:ProcessedRetentionDays", fallback.ProcessedRetentionDays, InboundReplySpoolOptions.MinRetentionDays, InboundReplySpoolOptions.MaxRetentionDays),
        ReadInt(configuration, "InboundReplies:FailedRetentionDays", fallback.FailedRetentionDays, InboundReplySpoolOptions.MinRetentionDays, InboundReplySpoolOptions.MaxRetentionDays),
        ReadInt(configuration, "InboundReplies:MaxFilesPerPoll", fallback.MaxFilesPerPoll, InboundReplySpoolOptions.MinFilesPerPoll, InboundReplySpoolOptions.MaxFilesPerPollLimit));
}

static PostfixDeliveryAutomationSettingsOptions ReadPostfixDeliveryAutomationSettingsOptions(IConfiguration configuration)
{
    var settingsPath = configuration["PostfixDelivery:SettingsPath"]
        ?? configuration["PostfixDelivery__SettingsPath"]
        ?? configuration["PISMOLET_POSTFIX_DELIVERY_SETTINGS_PATH"]
        ?? Environment.GetEnvironmentVariable("PISMOLET_POSTFIX_DELIVERY_SETTINGS_PATH")
        ?? PostfixDeliveryAutomationSettingsOptions.ProductionDefault.SettingsPath;
    var intervalSeconds = ReadInt(
        configuration,
        "PostfixDelivery:ReaderIntervalSeconds",
        PostfixDeliveryAutomationSettings.DefaultIntervalSeconds,
        PostfixDeliveryAutomationSettings.MinIntervalSeconds,
        PostfixDeliveryAutomationSettings.MaxIntervalSeconds);
    return new PostfixDeliveryAutomationSettingsOptions(settingsPath, intervalSeconds);
}

static int ReadInt(IConfiguration configuration, string key, int fallback, int min, int max)
{
    var value = configuration[key] ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)];
    return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
}

static long ReadLong(IConfiguration configuration, string key, long fallback, long min, long max)
{
    var value = configuration[key] ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)];
    return long.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
}

static bool ReadBool(IConfiguration configuration, string key, bool fallback)
{
    var value = configuration[key] ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)];
    return bool.TryParse(value, out var parsed) ? parsed : fallback;
}

public partial class Program;

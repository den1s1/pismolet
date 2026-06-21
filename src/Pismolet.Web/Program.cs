using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.DependencyInjection;

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

var adminEmails = ReadAdminEmails(builder.Configuration);

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
            return !string.IsNullOrWhiteSpace(email) && adminEmails.Contains(email);
        }));
});
builder.Services.AddPismoletWebServices(builder.Configuration);
builder.Services.AddPismoletEfSendingStorage(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment() && !isRunningUnderTests)
{
    app.Services.MigratePismoletDatabase();
    app.Services.SeedPismoletDevData();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHomeEndpoints();
app.MapAccountEndpoints();
app.MapDashboardEndpoints();
app.MapProfileEndpoints();
app.MapPaymentEndpoints();
app.MapCheckEndpoints();
app.MapSendEndpoints();
app.MapAdminEndpoints();
app.MapUnsubscribeEndpoints();
app.MapWebhookEndpoints();
app.MapInboundReplyEndpoints();

if (app.Environment.IsDevelopment())
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

static IReadOnlySet<string> ReadAdminEmails(IConfiguration configuration)
{
    var values = new List<string>();
    values.AddRange(Split(configuration["Admin:AllowedEmails"]));
    values.AddRange(Split(configuration["Admin:Emails"]));
    values.AddRange(Split(configuration["Pismolet:AdminEmails"]));
    values.AddRange(Split(configuration["PISMOLET_ADMIN_EMAILS"]));
    values.AddRange(Split(Environment.GetEnvironmentVariable("PISMOLET_ADMIN_EMAILS")));

    foreach (var child in configuration.GetSection("Admin:AllowedEmails").GetChildren())
    {
        values.AddRange(Split(child.Value));
    }

    return values
        .Select(email => email.Trim().ToLowerInvariant())
        .Where(email => !string.IsNullOrWhiteSpace(email))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

static IEnumerable<string> Split(string? value) => string.IsNullOrWhiteSpace(value)
    ? Array.Empty<string>()
    : value.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

public partial class Program
{
}

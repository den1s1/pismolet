using Microsoft.AspNetCore.Authentication.Cookies;
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

builder.Services.AddAuthorization();
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

public partial class Program
{
}

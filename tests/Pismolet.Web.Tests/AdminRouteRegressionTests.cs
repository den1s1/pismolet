using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pismolet.Web.Tests;

public sealed class AdminRouteRegressionTests
{
    private const string AdminEmail = "admin-routes@example.test";
    private const string NonAdminEmail = "not-admin-routes@example.test";

    [Theory]
    [InlineData("/admin", "Пользователи")]
    [InlineData("/admin/users", "Пользователи")]
    [InlineData("/admin/recipients", "Получатели")]
    [InlineData("/admin/campaigns", "Кампании")]
    [InlineData("/admin/payments", "Оплаты")]
    [InlineData("/admin/settings", "Настройки сервиса")]
    public async Task Admin_top_level_routes_are_available_and_not_ambiguous(string path, string expectedText)
    {
        using var factory = CreateAuthorizedFactory();
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, html);
        Assert.Contains("Письмолёт", html);
    }

    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/admin/recipients")]
    [InlineData("/admin/campaigns")]
    [InlineData("/admin/payments")]
    [InlineData("/admin/settings")]
    public async Task Non_admin_cannot_open_admin_top_level_routes(string path)
    {
        using var factory = CreateAuthorizedFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, NonAdminEmail);

        var response = await client.GetAsync(path);

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"Expected forbidden or redirect for {path}, got {(int)response.StatusCode} {response.StatusCode}.");
    }

    private static WebApplicationFactory<Program> CreateAuthorizedFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Admin:AllowedEmails"] = AdminEmail,
                    ["App:PublicBaseUrl"] = "https://app.pismolet.ru",
                    ["MailProvider"] = "Smtp",
                    ["Sending:Queue"] = "Inline",
                    ["Persistence:Provider"] = "InMemory",
                    ["Smtp:Host"] = "smtp.timeweb.ru",
                    ["Smtp:Port"] = "587",
                    ["Smtp:SecureSocketOptions"] = "StartTls",
                    ["Hangfire:WorkerCount"] = "1",
                    ["InboundReplies:Domain"] = "reply.pismolet.ru",
                    ["InboundReplies:TokenLifetimeDays"] = "180",
                    ["Unsubscribe:TokenLifetimeDays"] = "90"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            });
        });

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, email);
        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string EmailHeaderName = "X-Test-Email";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Request.Headers[EmailHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(email))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, email),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, email)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

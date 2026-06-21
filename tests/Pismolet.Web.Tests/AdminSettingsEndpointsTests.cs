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

public sealed class AdminSettingsEndpointsTests
{
    private const string AdminEmail = "admin-settings@example.test";

    [Fact]
    public async Task Admin_settings_page_shows_configuration_groups_and_values()
    {
        using var factory = CreateAuthorizedFactory();
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/settings");

        Assert.Contains("Настройки сервиса", html);
        Assert.Contains("Цены и биллинг", html);
        Assert.Contains("Лимиты клиентов", html);
        Assert.Contains("Правила модерации", html);
        Assert.Contains("SMTP и отправка", html);
        Assert.Contains("Системные настройки", html);
        Assert.Contains("Admin allowlist", html);
        Assert.Contains("https://app.pismolet.ru", html);
        Assert.Contains("smtp.timeweb.ru", html);
        Assert.Contains(AdminEmail, html);
    }

    [Fact]
    public async Task Admin_settings_actions_redirect_back_with_action_marker()
    {
        using var factory = CreateAuthorizedFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, AdminEmail);

        var response = await client.PostAsync("/admin/settings/smtp-test", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/settings", response.Headers.Location?.ToString() ?? string.Empty);
        Assert.Contains("action=smtp-test", response.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Admin_settings_action_marker_shows_status_message()
    {
        using var factory = CreateAuthorizedFactory();
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/settings?action=smtp");

        Assert.Contains("SMTP-настройки сохранены", html);
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

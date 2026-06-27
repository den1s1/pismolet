using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pismolet.Web.Application.Common;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class AdminReplyEndpointsTests
{
    private const string AdminEmail = "admin-replies@example.test";

    [Fact]
    public async Task Admin_replies_page_is_available_to_admin_without_raw_payload()
    {
        using var factory = CreateAuthorizedAdminFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/admin/replies");
        var html = await response.Content.ReadAsStringAsync();
        var normalizedHtml = html.ToLowerInvariant();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ответы", html);
        Assert.Contains("Получен", html);
        Assert.Contains("Статус", html);
        Assert.DoesNotContain("RawPayload", html);
        Assert.DoesNotContain("raw mime", normalizedHtml);
        Assert.DoesNotContain("ReplyToken", html);
    }

    private static WebApplicationFactory<Program> CreateAuthorizedAdminFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.AddSingleton<IAdminAccessService>(new TestAdminAccessService(AdminEmail));
            });
        });

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, AdminEmail);
        return client;
    }

    private sealed class TestAdminAccessService(string adminEmail) : IAdminAccessService
    {
        public bool IsAdminEmail(string? email) => string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase);

        public bool IsConfigAdminEmail(string? email) => IsAdminEmail(email);

        public bool IsManagedAdminEmail(string? email) => false;

        public IReadOnlyCollection<string> ListManagedAdminEmails() => Array.Empty<string>();

        public void GrantAdmin(string email, string grantedByEmail)
        {
        }

        public bool TryRevokeAdmin(string email, string revokedByEmail, out string error)
        {
            error = "Тестовый admin access service не поддерживает отзыв прав.";
            return false;
        }
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

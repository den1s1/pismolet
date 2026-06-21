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
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Tests;

public sealed class AdminEndpointsTests
{
    private const string AdminEmail = "admin@example.test";
    private const string OwnerEmail = "owner@example.test";
    private const string OtherEmail = "other@example.test";

    [Fact]
    public async Task Anonymous_user_is_redirected_from_admin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Admin_users_page_shows_sidebar_stats_and_users()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        SeedUser(factory, OtherEmail, "Other User");
        SeedMailing(factory, OwnerEmail, "Owner campaign");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/users");

        Assert.Contains("Пользователи", html);
        Assert.Contains("Получатели", html);
        Assert.Contains("Кампании", html);
        Assert.Contains("Оплаты", html);
        Assert.Contains("Настройки", html);
        Assert.Contains("Администратор", html);
        Assert.Contains(AdminEmail, html);
        Assert.Contains(OwnerEmail, html);
        Assert.Contains(OtherEmail, html);
        Assert.Contains("Owner campaign", html);
        Assert.Contains("Экспорт CSV - скоро", html);
        Assert.Contains("Кампаний всего", html);
    }

    [Fact]
    public async Task Admin_users_page_filters_by_search()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        SeedUser(factory, OtherEmail, "Other User");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/users?q=owner");

        Assert.Contains(OwnerEmail, html);
        Assert.DoesNotContain(OtherEmail, html);
    }

    [Fact]
    public async Task Admin_user_profile_shows_limits_and_user_mailings()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        SeedMailing(factory, OwnerEmail, "Owner campaign");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/users/" + Uri.EscapeDataString(OwnerEmail));

        Assert.Contains("Профиль пользователя", html);
        Assert.Contains("Owner User", html);
        Assert.Contains(OwnerEmail, html);
        Assert.Contains("Дневной лимит", html);
        Assert.Contains("Премодерация", html);
        Assert.Contains("Рассылки пользователя", html);
        Assert.Contains("Owner campaign", html);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });

    private static WebApplicationFactory<Program> CreateAuthorizedFactory() =>
        CreateFactory().WithWebHostBuilder(builder =>
        {
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

    private static void SeedUser(WebApplicationFactory<Program> factory, string email, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var result = accounts.Register(new RegisterUserCommand(email, "Password123!", displayName), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static void SeedMailing(WebApplicationFactory<Program> factory, string ownerEmail, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(ownerEmail, subject), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static RequestMetadata Request() => new("127.0.0.1", "admin-endpoint-tests");

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

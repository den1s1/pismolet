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
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

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
    public async Task Authenticated_non_admin_user_is_forbidden_from_admin()
    {
        using var factory = CreateAuthorizedFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, OwnerEmail);

        var response = await client.GetAsync("/admin/users");

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, $"Unexpected status: {response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.Contains("/account/login", response.Headers.Location?.ToString() ?? string.Empty);
        }
    }

    [Fact]
    public async Task Non_admin_menu_does_not_show_admin_link()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);

        var html = await client.GetStringAsync("/dashboard");

        Assert.Contains("href='/profile'", html);
        Assert.DoesNotContain("href='/admin'", html);
        Assert.DoesNotContain("Админка</a>", html);
    }

    [Fact]
    public async Task Admin_menu_shows_admin_link()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, AdminEmail, "Admin User");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/dashboard");

        Assert.Contains("href='/admin'", html);
        Assert.Contains("Админка</a>", html);
    }

    [Fact]
    public async Task Admin_users_page_shows_compact_user_table()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        SeedUser(factory, OtherEmail, "Other User");
        SeedMailing(factory, OwnerEmail, "Owner campaign");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/users");

        AssertAdminSidebar(html);
        Assert.Contains("Пользователи", html);
        Assert.Contains("Администратор", html);
        Assert.Contains(AdminEmail, html);
        Assert.Contains("<th>ФИО</th><th>Email</th><th>Телефон</th>", html);
        Assert.Contains("Owner User", html);
        Assert.Contains("Other User", html);
        Assert.Contains(OwnerEmail, html);
        Assert.Contains(OtherEmail, html);
        Assert.Contains("+79990000000", html);
        Assert.Contains("Экспорт CSV - скоро", html);
        Assert.Contains("href='/admin/users/owner%40example.test'", html);
        Assert.DoesNotContain("Кампаний всего", html);
        Assert.DoesNotContain("Реальные аккаунты, статусы email", html);
        Assert.DoesNotContain("<th>Клиент</th>", html);
        Assert.DoesNotContain("<th>Email подтверждён</th>", html);
        Assert.DoesNotContain("<td><a class='admin-link' href='/admin/users/owner%40example.test'>Профиль</a></td>", html);
    }

    [Fact]
    public async Task Admin_campaigns_page_uses_same_sidebar()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        SeedMailing(factory, OwnerEmail, "Owner campaign");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/campaigns");

        AssertAdminSidebar(html);
    }

    [Fact]
    public async Task Admin_clients_redirects_to_users()
    {
        using var factory = CreateAuthorizedFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, AdminEmail);

        var response = await client.GetAsync("/admin/clients");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/users", response.Headers.Location?.OriginalString);
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

    [Fact]
    public async Task Admin_recipients_page_shows_statuses_and_filters_by_email()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        SeedUser(factory, OtherEmail, "Other User");
        SeedRecipientMailing(factory, OwnerEmail, "Lead campaign", "lead@example.test");
        SeedRecipientMailing(factory, OtherEmail, "Other lead campaign", "lead@example.test");
        SeedRecipientMailing(factory, OtherEmail, "Blocked campaign", "blocked@example.test");
        SuppressRecipient(factory, "blocked@example.test");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/recipients");

        Assert.Contains("Получатели", html);
        Assert.Contains("lead@example.test", html);
        Assert.Contains("Активен", html);
        Assert.Contains("blocked@example.test", html);
        Assert.Contains("Заблокирован вручную", html);
        Assert.Contains("Клиентов", html);

        var filtered = await client.GetStringAsync("/admin/recipients?q=blocked");
        Assert.Contains("blocked@example.test", filtered);
        Assert.DoesNotContain("lead@example.test", filtered);
    }

    [Fact]
    public async Task Admin_recipient_profile_shows_lists_and_allows_global_suppression()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Owner User");
        SeedRecipientMailing(factory, OwnerEmail, "Lead campaign", "lead@example.test");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/recipients/" + Uri.EscapeDataString("lead@example.test"));

        Assert.Contains("Профиль получателя", html);
        Assert.Contains("lead@example.test", html);
        Assert.Contains("Списки пользователей", html);
        Assert.Contains(OwnerEmail, html);
        Assert.Contains("Lead campaign", html);
        Assert.Contains("Добавить глобальную отписку", html);

        var response = await client.PostAsync("/admin/recipients/" + Uri.EscapeDataString("lead@example.test") + "/suppress", new StringContent(string.Empty));
        response.EnsureSuccessStatusCode();
        var updated = await client.GetStringAsync("/admin/recipients/" + Uri.EscapeDataString("lead@example.test"));
        Assert.Contains("Заблокирован вручную", updated);
    }

    private static void AssertAdminSidebar(string html)
    {
        Assert.Contains("href='/admin'>Dashboard</a>", html);
        Assert.Contains("href='/admin/users'>Пользователи</a>", html);
        Assert.Contains("href='/admin/campaigns'>Рассылки</a>", html);
        Assert.Contains("href='/admin/moderation'>Модерация</a>", html);
        Assert.Contains("href='/admin/imports'>Импорты</a>", html);
        Assert.Contains("href='/admin/payments'>Платежи</a>", html);
        Assert.Contains("href='/admin/recipients'>Отписки</a>", html);
        Assert.Contains("href='/admin/complaints'>Жалобы</a>", html);
        Assert.Contains("href='/admin/delivery-errors'>Ошибки доставки</a>", html);
        Assert.Contains("href='/admin/replies'>Ответы</a>", html);
        Assert.Contains("href='/admin/audit'>Audit log</a>", html);
        Assert.Contains("href='/admin/settings'>Настройки</a>", html);
        Assert.DoesNotContain("href='/admin/settings/mvp'>Настройки</a>", html);
        Assert.DoesNotContain("href='/admin/clients'>Клиенты</a>", html);
        Assert.DoesNotContain("admin-sidebar-links", html);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Admin:AllowedEmails"] = AdminEmail
                });
            });
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
        var result = accounts.Register(new RegisterUserCommand(email, "Password123!", displayName, TestPhone(email)), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static string TestPhone(string email) => email.Trim().ToLowerInvariant() switch
    {
        OwnerEmail => "+79990000000",
        OtherEmail => "+79990000001",
        AdminEmail => "+79990000002",
        _ => "+79990000003"
    };

    private static void SeedMailing(WebApplicationFactory<Program> factory, string ownerEmail, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(ownerEmail, subject), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static void SeedRecipientMailing(WebApplicationFactory<Program> factory, string ownerEmail, string subject, string recipientEmail)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingRepository>();
        var normalized = recipientEmail.Trim().ToLowerInvariant();
        var mailing = Mailing.Draft(ownerEmail, subject).WithImportResult(
            new ImportStats(1, 1, 0, 0, 0),
            [Recipient.Accepted(recipientEmail, normalized)]);
        Assert.True(mailings.TryAdd(mailing));
    }

    private static void SuppressRecipient(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var suppressions = scope.ServiceProvider.GetRequiredService<IGlobalSuppressionRepository>();
        suppressions.Add(email);
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

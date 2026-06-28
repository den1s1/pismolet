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
using Pismolet.Web.Application.Admin;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;

namespace Pismolet.Web.Tests;

public sealed class AdminUsersManagementTests
{
    private const string RootAdminEmail = "root-admin@example.test";
    private const string PromotedEmail = "promoted-user@example.test";

    [Fact]
    public async Task Admin_can_grant_and_revoke_admin_rights_from_user_profile()
    {
        var adminStore = Path.Combine(Path.GetTempPath(), $"pismolet-admins-{Guid.NewGuid():N}.txt");
        using var factory = CreateAuthorizedFactory(adminStore);
        SeedUser(factory, RootAdminEmail, "Root Admin");
        SeedUser(factory, PromotedEmail, "Promoted User");
        using var adminClient = CreateAuthenticatedClient(factory, RootAdminEmail, allowAutoRedirect: false);
        var encoded = Uri.EscapeDataString(PromotedEmail);

        var profileBefore = await adminClient.GetStringAsync($"/admin/users/{encoded}");
        Assert.Contains("Пользователь", profileBefore);
        Assert.Contains("Сделать администратором", profileBefore);

        var grant = await adminClient.PostAsync($"/admin/users/{encoded}/admin/grant", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Redirect, grant.StatusCode);
        Assert.Equal($"/admin/users/{encoded}", grant.Headers.Location?.OriginalString);

        var profileAfterGrant = await adminClient.GetStringAsync($"/admin/users/{encoded}");
        Assert.Contains("Администратор", profileAfterGrant);
        Assert.Contains("Источник прав: назначено через админку", profileAfterGrant);
        Assert.Contains("Снять админские права", profileAfterGrant);

        using var promotedClient = CreateAuthenticatedClient(factory, PromotedEmail, allowAutoRedirect: false);
        var promotedAdminPage = await promotedClient.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.OK, promotedAdminPage.StatusCode);

        var selfRevoke = await promotedClient.PostAsync($"/admin/users/{encoded}/admin/revoke", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var selfRevokeHtml = await selfRevoke.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, selfRevoke.StatusCode);
        Assert.Contains("Нельзя снять админские права с самого себя", selfRevokeHtml);

        var revoke = await adminClient.PostAsync($"/admin/users/{encoded}/admin/revoke", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);

        var profileAfterRevoke = await adminClient.GetStringAsync($"/admin/users/{encoded}");
        Assert.Contains("Админские права не назначены", profileAfterRevoke);
        Assert.Contains("Сделать администратором", profileAfterRevoke);
    }

    [Fact]
    public async Task Config_admin_cannot_be_revoked_from_user_profile()
    {
        var adminStore = Path.Combine(Path.GetTempPath(), $"pismolet-admins-{Guid.NewGuid():N}.txt");
        using var factory = CreateAuthorizedFactory(adminStore);
        SeedUser(factory, RootAdminEmail, "Root Admin");
        using var client = CreateAuthenticatedClient(factory, RootAdminEmail, allowAutoRedirect: false);
        var encoded = Uri.EscapeDataString(RootAdminEmail);

        var profile = await client.GetStringAsync($"/admin/users/{encoded}");
        Assert.Contains("Администратор · конфиг", profile);
        Assert.Contains("С себя снять админские права нельзя", profile);
        Assert.DoesNotContain("Снять админские права", profile);
    }

    [Fact]
    public async Task Admin_profile_allows_updating_individual_notification_settings()
    {
        var adminStore = Path.Combine(Path.GetTempPath(), $"pismolet-admins-{Guid.NewGuid():N}.txt");
        using var factory = CreateAuthorizedFactory(adminStore);
        SeedUser(factory, RootAdminEmail, "Root Admin");
        SeedUser(factory, PromotedEmail, "Promoted User");
        using var client = CreateAuthenticatedClient(factory, RootAdminEmail, allowAutoRedirect: false);
        var encoded = Uri.EscapeDataString(PromotedEmail);
        await client.PostAsync($"/admin/users/{encoded}/admin/grant", new FormUrlEncodedContent(new Dictionary<string, string>()));

        var profileBefore = await client.GetStringAsync($"/admin/users/{encoded}");
        Assert.Contains("Уведомления на email", profileBefore);
        Assert.Contains("name='notifyUserRegistered'", profileBefore);
        Assert.Contains("name='notifyMailingCreated'", profileBefore);
        Assert.DoesNotContain("name='notifyUserRegistered' value='on' checked", profileBefore);
        Assert.DoesNotContain("name='notifyMailingPaid' value='on' checked", profileBefore);

        var response = await client.PostAsync($"/admin/users/{encoded}/notifications", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["notifyUserRegistered"] = "on",
            ["notifyMailingPaid"] = "on"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/users/{encoded}", response.Headers.Location?.OriginalString);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAdminNotificationSettingsRepository>();
        var settings = repository.Get(PromotedEmail);
        Assert.True(settings.UserRegistered);
        Assert.False(settings.MailingCreated);
        Assert.False(settings.MailingSubmittedToModeration);
        Assert.True(settings.MailingPaid);

        var profileAfter = await client.GetStringAsync($"/admin/users/{encoded}");
        Assert.Contains("name='notifyUserRegistered' value='on' checked", profileAfter);
        Assert.Contains("name='notifyMailingPaid' value='on' checked", profileAfter);
        Assert.DoesNotContain("name='notifyMailingCreated' value='on' checked", profileAfter);
    }

    [Fact]
    public async Task Admin_can_remove_non_admin_user_from_profile()
    {
        const string targetEmail = "removable-user@example.test";
        var adminStore = Path.Combine(Path.GetTempPath(), $"pismolet-admins-{Guid.NewGuid():N}.txt");
        using var factory = CreateAuthorizedFactory(adminStore);
        SeedUser(factory, RootAdminEmail, "Root Admin");
        SeedUser(factory, targetEmail, "Removable User");
        SeedMailing(factory, targetEmail, "Campaign to remove");
        using var client = CreateAuthenticatedClient(factory, RootAdminEmail, allowAutoRedirect: false);
        var encoded = Uri.EscapeDataString(targetEmail);

        var response = await client.PostAsync($"/admin/users/{encoded}/remove", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/users?action=removed&removed={encoded}", response.Headers.Location?.OriginalString);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingRepository>();
        Assert.Null(users.GetByEmail(targetEmail));
        Assert.Empty(mailings.ListForOwner(targetEmail));
    }

    private static WebApplicationFactory<Program> CreateAuthorizedFactory(string adminStorePath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Admin:AllowedEmails"] = RootAdminEmail,
                    ["Admin:ManagedAdminsPath"] = adminStorePath
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

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory, string email, bool allowAutoRedirect = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, email);
        return client;
    }

    private static void SeedUser(WebApplicationFactory<Program> factory, string email, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var result = accounts.Register(new RegisterUserCommand(email, "PassForTests2026!", displayName, TestPhone(email)), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static void SeedMailing(WebApplicationFactory<Program> factory, string ownerEmail, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(ownerEmail, subject), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static string TestPhone(string email) => email.Trim().ToLowerInvariant() switch
    {
        RootAdminEmail => "+79990000100",
        PromotedEmail => "+79990000101",
        "removable-user@example.test" => "+79990000103",
        _ => "+79990000102"
    };

    private static RequestMetadata Request() => new("127.0.0.1", "admin-users-management-tests");

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

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

public sealed class AdminCampaignEndpointsTests
{
    private const string AdminEmail = "admin-campaigns@example.test";
    private const string OwnerEmail = "campaign-owner@example.test";

    [Fact]
    public async Task Admin_campaigns_page_shows_campaigns_and_filters_by_search()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Campaign Owner");
        SeedMailing(factory, OwnerEmail, "Spring campaign");
        SeedMailing(factory, OwnerEmail, "Hidden campaign");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/campaigns?q=spring");

        Assert.Contains("Кампании", html);
        Assert.Contains("Все рассылки сервиса", html);
        Assert.Contains("Spring campaign", html);
        Assert.Contains(OwnerEmail, html);
        Assert.Contains("Получателей к отправке", html);
        Assert.Contains("Стоимость", html);
        Assert.DoesNotContain("Hidden campaign", html);
    }

    [Fact]
    public async Task Admin_campaign_profile_shows_timeline_actions_and_send_log()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Campaign Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Profile campaign");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync($"/admin/campaigns/{mailingId}");

        Assert.Contains("Профиль кампании", html);
        Assert.Contains("Profile campaign", html);
        Assert.Contains("Таймлайн кампании", html);
        Assert.Contains("Текст письма", html);
        Assert.Contains("Лог отправки", html);
        Assert.Contains("Поставить на паузу", html);
        Assert.Contains("Повторно проверить письмо", html);
        Assert.Contains("Вернуть на модерацию", html);
        Assert.Contains("Отменить кампанию", html);
    }

    [Fact]
    public async Task Admin_campaign_profile_shows_open_analytics_without_read_wording()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Campaign Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Open analytics campaign");
        SeedOpenedSendEvent(factory, mailingId, "opened@example.test");
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync($"/admin/campaigns/{mailingId}");

        Assert.Contains("Открытия", html);
        Assert.Contains("Открыто, получателей", html);
        Assert.Contains("Открытий всего", html);
        Assert.Contains("Последнее открытие", html);
        Assert.Contains("opened@example.test", html);
        Assert.Contains(">Да<", html);
        Assert.DoesNotContain("Прочитано", html);
        Assert.DoesNotContain("прочитано", html);
    }

    [Fact]
    public async Task Admin_campaign_pause_action_redirects_back_to_profile_with_status_message()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Campaign Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Pause campaign");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, AdminEmail);

        var response = await client.PostAsync($"/admin/campaigns/{mailingId}/pause", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/admin/campaigns/{mailingId}", response.Headers.Location?.ToString() ?? string.Empty);
        Assert.Contains("action=paused", response.Headers.Location?.ToString() ?? string.Empty);
    }

    private static WebApplicationFactory<Program> CreateAuthorizedFactory() =>
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

    private static Guid SeedMailing(WebApplicationFactory<Program> factory, string ownerEmail, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(ownerEmail, subject), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);
        return result.Mailing.Id;
    }

    private static void SeedOpenedSendEvent(WebApplicationFactory<Program> factory, Guid mailingId, string recipientEmail)
    {
        using var scope = factory.Services.CreateScope();
        var sends = scope.ServiceProvider.GetRequiredService<ISendEventRepository>();
        var sendEvent = SendEvent
            .Pending(mailingId, OwnerEmail, recipientEmail)
            .MarkAccepted($"LocalSmtp{SendEvent.ProviderEnvelopeSeparator}{Guid.NewGuid():N}")
            .MarkOpened(new DateTimeOffset(2026, 6, 22, 10, 45, 0, TimeSpan.Zero));

        sends.Save(sendEvent);
    }

    private static RequestMetadata Request() => new("127.0.0.1", "admin-campaign-endpoint-tests");

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

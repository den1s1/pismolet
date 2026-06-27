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
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class SendEndpointUiTests
{
    private const string OwnerEmail = "send-ui@example.test";

    [Fact]
    public async Task Send_page_keeps_main_screen_simple_and_collapses_technical_report()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedApprovedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/send");

        Assert.Contains("Готово к запуску", html);
        Assert.Contains("Всего получателей", html);
        Assert.Contains("Отправлено", html);
        Assert.Contains("Ошибки отправки", html);
        Assert.Contains("Ответов", html);
        Assert.Contains("Запустить отправку", html);
        Assert.Contains("<details class='detailed-report'>", html);
        Assert.DoesNotContain("<details class='detailed-report' open>", html);
        Assert.DoesNotContain("Dev-сводка событий", html);

        var reportStart = html.IndexOf("<details class='detailed-report'>", StringComparison.Ordinal);
        Assert.True(reportStart > 0, "Detailed report was not found.");
        var mainScreen = html[..reportStart];
        Assert.DoesNotContain("Список получателей", mainScreen);
        Assert.DoesNotContain("Доставка по получателям", mainScreen);
        Assert.DoesNotContain("Переходы по ссылкам", mainScreen);
        Assert.DoesNotContain("ProviderMessageId", mainScreen);
        Assert.DoesNotContain("raw provider payload", mainScreen);
    }

    [Fact]
    public async Task Review_required_send_page_keeps_launch_button_disabled()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailingWithStatus(factory, MailingStatus.ReviewRequired);
        using var client = CreateAuthenticatedClient(factory);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/send");

        Assert.Contains("Рассылка на модерации", html);
        Assert.Contains("<button class='button' disabled>Запустить отправку</button>", html);
        Assert.DoesNotContain($"action='/mailings/{mailingId}/send/start'", html);
    }

    [Fact]
    public async Task Legacy_checks_page_redirects_to_send_screen()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedApprovedMailing(factory);
        using var client = CreateAuthenticatedClient(factory, allowAutoRedirect: false);

        var response = await client.GetAsync($"/mailings/{mailingId}/checks");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/send", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Send_start_error_keeps_user_screen_simple_and_report_collapsed()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedApprovedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsync($"/mailings/{mailingId}/send/start", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Сначала оплатите рассылку", html);
        Assert.Contains("Запустить отправку", html);
        Assert.Contains("<details class='detailed-report'>", html);
        Assert.DoesNotContain("<details class='detailed-report' open>", html);
        Assert.DoesNotContain("Dev-сводка событий", html);
    }

    private static WebApplicationFactory<Program> CreateAuthorizedFactory() =>
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
            });
        });

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory, bool allowAutoRedirect = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, OwnerEmail);
        return client;
    }

    private static void SeedUser(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        users.Save(new UserProfile(OwnerEmail, RoleNames.Sender));
    }

    private static Guid SeedApprovedMailing(WebApplicationFactory<Program> factory) =>
        SeedMailingWithStatus(factory, MailingStatus.Approved);

    private static Guid SeedMailingWithStatus(WebApplicationFactory<Program> factory, MailingStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingRepository>();
        var mailing = Mailing.CreateDraft(OwnerEmail, "UI report campaign");
        mailing = mailing.WithContent("UI report campaign", "<p>Hello report</p>", "Hello report", Array.Empty<MailingAttachment>());
        mailing = mailing.WithRecipients(new[]
        {
            MailingRecipient.Accepted("lead@example.test", null, new Dictionary<string, string>())
        });
        mailing = status switch
        {
            MailingStatus.ReviewRequired => mailing.SubmitForReview(DateTimeOffset.UtcNow),
            MailingStatus.Approved => mailing.SubmitForReview(DateTimeOffset.UtcNow).Approve(DateTimeOffset.UtcNow),
            _ => mailing.WithStatus(status)
        };
        mailings.Save(mailing);
        return mailing.Id;
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
            var email = Request.Headers[EmailHeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(email))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, RoleNames.Sender)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

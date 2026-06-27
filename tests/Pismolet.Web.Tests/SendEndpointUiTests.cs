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
    public async Task Send_page_keeps_detailed_report_open_when_recipient_page_changes()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedApprovedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/send?recipientPage=2");

        Assert.Contains("<details class='detailed-report' open>", html);
        Assert.Contains("<details id='recipient-list' open>", html);
        Assert.Contains("Список получателей", html);
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
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var result = accounts.Register(new RegisterUserCommand(OwnerEmail, "PassForTests2026!", "Send UI", "+79990000000"), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static Guid SeedApprovedMailing(WebApplicationFactory<Program> factory)
    {
        return SeedMailingWithStatus(factory, MailingStatus.Approved);
    }

    private static Guid SeedMailingWithStatus(WebApplicationFactory<Program> factory, MailingStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var repository = scope.ServiceProvider.GetRequiredService<IMailingRepository>();
        var result = mailings.CreateDraft(new CreateMailingCommand(OwnerEmail, "Send UI campaign"), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);

        var mailing = repository.GetForOwner(result.Mailing.Id, OwnerEmail);
        Assert.NotNull(mailing);

        var separator = Convert.ToChar(64);
        var recipients = Enumerable.Range(1, 55)
            .Select(index =>
            {
                var recipient = $"recipient{index:00}{separator}example.test";
                return Recipient.Accepted(recipient, recipient, rowNumber: index + 1);
            })
            .ToArray();
        var declaration = new MailingDeclaration(
            mailing.Id,
            OwnerEmail,
            BaseSource.Customers,
            IsBaseLegalityConfirmed: true,
            IsAdvertisingConsentConfirmed: false,
            BaseDeclarationText.CurrentVersion,
            DateTimeOffset.UtcNow,
            "127.0.0.1",
            "send-ui-tests");
        var draft = MailingMessageDraft.Create(
            "Библиотека №5",
            "Приглашаем на встречу",
            "Здравствуйте!",
            MessageType.Transactional,
            DateTimeOffset.UtcNow);
        var ready = mailing
            .WithImportResult(new ImportStats(recipients.Length, recipients.Length, 0, 0, 0), recipients)
            .WithDeclaration(declaration)
            .WithMessageDraft(draft)
            .WithStatus(status);
        repository.Update(ready);
        return mailing.Id;
    }

    private static RequestMetadata Request() => new("127.0.0.1", "send-endpoint-ui-tests");

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

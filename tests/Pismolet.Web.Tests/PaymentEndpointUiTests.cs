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

public sealed class PaymentEndpointUiTests
{
    private const string OwnerEmail = "payment-ui-owner@example.test";

    [Fact]
    public async Task Payment_step_shows_readonly_base_summary_and_single_final_confirmation()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Payment UI Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Payment UI campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        await SaveMessage(client, mailingId);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/payment");

        Assert.Contains("href='/checkbox.css?v=", html);
        Assert.Contains("payment-legal-summary", html);
        Assert.Contains("Подтверждения базы", html);
        Assert.Contains("Источник базы", html);
        Assert.Contains("Тип письма", html);
        Assert.Contains("Правомерность базы", html);
        Assert.Contains("Рекламное согласие", html);
        Assert.Contains("Финальное подтверждение", html);
        Assert.Contains("<label class='check'><input type='checkbox' name='campaignLaunchConfirmation'><span>", html);
        Assert.DoesNotContain("name='paymentBaseLegality'", html);
        Assert.DoesNotContain("name='paymentBaseOwnership'", html);
        Assert.DoesNotContain("name='advertisingConsent'", html);
        Assert.Contains($"/legal/payment-and-refund?returnUrl=/mailings/{mailingId}/payment", html);
        Assert.Contains("Оплатить 1 ₽", html);
        Assert.DoesNotContain("Оплатить 1 ₽ через Robokassa", html);
    }

    [Fact]
    public async Task Advertising_payment_without_saved_advertising_consent_is_blocked_by_endpoint()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Payment UI Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Advertising without consent campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        ForceAdvertisingDraftWithoutAdvertisingConsent(factory, mailingId);

        var paymentHtml = await client.GetStringAsync($"/mailings/{mailingId}/payment");

        Assert.Contains("Нужно подтвердить рекламное согласие", paymentHtml);
        Assert.Contains($"href='/mailings/{mailingId}/recipients'", paymentHtml);
        Assert.DoesNotContain("name='advertisingConsent'", paymentHtml);

        using var confirmation = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["campaignLaunchConfirmation"] = "on"
        });
        var start = await client.PostAsync($"/mailings/{mailingId}/payment/fake-start", confirmation);
        var startHtml = await start.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Contains("Для рекламной рассылки сначала подтвердите рекламное согласие", startHtml);
        Assert.DoesNotContain("Оплата через Robokassa", startHtml);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.NotEqual(MailingStatus.PaymentPending, mailing.Status);
    }

    private static async Task ImportAcceptedAddress(HttpClient client, Guid mailingId)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("reader@example.test"), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ConfirmBaseDeclaration(HttpClient client, Guid mailingId)
    {
        using var declarationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["baseSource"] = "Customers",
            ["baseLegality"] = "on",
            ["messageType"] = "Transactional"
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/declaration", declarationForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected declaration response: {(int)response.StatusCode}");
    }

    private static async Task SaveMessage(HttpClient client, Guid mailingId)
    {
        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "Приглашаем на встречу",
            ["body"] = "Здравствуйте!\n\nБудем рады видеть вас."
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected message response: {(int)response.StatusCode}");
    }

    private static void ForceAdvertisingDraftWithoutAdvertisingConsent(WebApplicationFactory<Program> factory, Guid mailingId)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMailingRepository>();
        var mailing = repository.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.NotNull(mailing.Declaration);

        var declaration = mailing.Declaration with { IsAdvertisingConsentConfirmed = false };
        var draft = MailingMessageDraft.Create(
            "Рекламный отправитель",
            "Рекламная рассылка",
            "Рекламный текст",
            MessageType.Advertising,
            DateTimeOffset.UtcNow);
        repository.Update(mailing.WithDeclaration(declaration).WithMessageDraft(draft));
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
        var result = accounts.Register(new RegisterUserCommand(email, "PassForTests2026!", displayName, "+79990000000"), Request());
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

    private static RequestMetadata Request() => new("127.0.0.1", "payment-endpoint-ui-tests");

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

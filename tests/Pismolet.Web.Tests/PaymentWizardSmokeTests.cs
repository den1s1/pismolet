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
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class PaymentWizardSmokeTests
{
    private const string OwnerEmail = "payment-smoke@example.test";

    [Fact]
    public async Task Payment_page_shows_cost_and_required_confirmations()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Payment smoke");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Transactional);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/payment");

        Assert.Contains("3. Проверьте расчёт и оплатите", html);
        Assert.Contains("Оплата будет только за письма, принятые к отправке", html);
        Assert.Contains("К оплате", html);
        Assert.Contains("name='campaignLaunchConfirmation'", html);
        Assert.Contains("name='paymentBaseLegality'", html);
        Assert.Contains("name='paymentBaseOwnership'", html);
        Assert.Contains("Правила оплаты, запуска и возвратов", html);
        Assert.Contains($"/legal/payment-and-refund?returnUrl=/mailings/{mailingId}/payment", html);
        Assert.Contains("Не списываем за исключённые", html);
        Assert.Contains("Оплатить", html);
        Assert.Contains("и запустить", html);
    }

    [Fact]
    public async Task Payment_start_requires_confirmations_and_then_sets_pending_status()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Payment start");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Transactional);
        var paymentStartPath = $"/mailings/{mailingId}/payment/" + "fake-start";

        var blocked = await client.PostAsync(paymentStartPath, new FormUrlEncodedContent(new Dictionary<string, string>()));
        var blockedHtml = await blocked.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("Подтвердите", blockedHtml);

        var ok = await client.PostAsync(paymentStartPath, PaymentConfirmations());
        var okHtml = await ok.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Тестовая оплата", okHtml);
        Assert.Contains("Правила оплаты, запуска и возвратов", okHtml);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(MailingStatus.PaymentPending, mailing.Status);
    }

    [Fact]
    public async Task Promo_message_requires_extra_confirmation_on_payment_step()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Promo payment start");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Advertising);
        var paymentStartPath = $"/mailings/{mailingId}/payment/" + "fake-start";

        var blocked = await client.PostAsync(paymentStartPath, PaymentConfirmations());
        var html = await blocked.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("Для промо-письма", html);
    }

    private static async Task Prepare(HttpClient client, Guid mailingId, MessageType messageType)
    {
        await client.PostAsync($"/mailings/{mailingId}/recipients", new MultipartFormDataContent { { new StringContent("first@example.test\nwrong\nFIRST@example.test"), "manualAddresses" } });
        var declarationFields = new Dictionary<string, string> { ["baseSource"] = "Customers", ["baseLegality"] = "on", ["messageType"] = messageType.ToString() };
        if (messageType == MessageType.Advertising) declarationFields["advertisingConsent"] = "on";
        await client.PostAsync($"/mailings/{mailingId}/declaration", new FormUrlEncodedContent(declarationFields));
        var message = await client.PostAsync($"/mailings/{mailingId}/message", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Sender",
            ["subject"] = "Subject",
            ["body"] = "Body"
        }));
        Assert.True(message.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK, $"Unexpected message response: {(int)message.StatusCode}");
        if (message.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.Equal($"/mailings/{mailingId}/payment", message.Headers.Location?.OriginalString);
        }
    }

    private static FormUrlEncodedContent PaymentConfirmations() => new(new Dictionary<string, string>
    {
        ["campaignLaunchConfirmation"] = "on",
        ["paymentBaseLegality"] = "on",
        ["paymentBaseOwnership"] = "on"
    });

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

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, OwnerEmail);
        return client;
    }

    private static void SeedUser(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var result = accounts.Register(new RegisterUserCommand(OwnerEmail, "TestPassword123!", "Payment Smoke", "+79990000000"), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static Guid SeedMailing(WebApplicationFactory<Program> factory, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(OwnerEmail, subject), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);
        return result.Mailing.Id;
    }

    private static RequestMetadata Request() => new("127.0.0.1", "payment-wizard-smoke-tests");

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string EmailHeaderName = "X-Test-Email";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Request.Headers[EmailHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(email)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, email), new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.Name, email) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}

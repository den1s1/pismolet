using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
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

public sealed class PaymentWizardSmokeTests
{
    private const string OwnerEmail = "payment-smoke@example.test";
    private static readonly Regex HiddenInputRegex = new("<input type='hidden' name='(?<name>[^']+)' value='(?<value>[^']*)'>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public async Task Payment_page_shows_cost_and_single_final_confirmation()
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
        Assert.Contains("payment-legal-summary", html);
        Assert.Contains("Подтверждения базы", html);
        Assert.Contains("name='campaignLaunchConfirmation'", html);
        Assert.DoesNotContain("name='paymentBaseLegality'", html);
        Assert.DoesNotContain("name='paymentBaseOwnership'", html);
        Assert.DoesNotContain("name='advertisingConsent'", html);
        Assert.Contains("Правила оплаты, запуска и возвратов", html);
        Assert.Contains($"/legal/payment-and-refund?returnUrl=/mailings/{mailingId}/payment", html);
        Assert.Contains("Не списываем за исключённые", html);
        Assert.Contains("Оплатить", html);
        Assert.DoesNotContain("через Robokassa", html);
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
        Assert.Contains("Оплата через Robokassa", okHtml);
        Assert.Contains("MerchantLogin", okHtml);
        Assert.Contains("ResultURL", okHtml);
        Assert.Contains("SuccessURL", okHtml);
        Assert.Contains("FailURL", okHtml);
        Assert.Contains("action='/payments/robokassa/fake/checkout'", okHtml);
        Assert.Contains("name='SignatureValue'", okHtml);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(MailingStatus.PaymentPending, mailing.Status);
    }

    [Fact]
    public async Task Robokassa_result_url_confirms_payment_after_signature_check()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Robokassa result");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Transactional);
        var startHtml = await StartPayment(client, mailingId);
        var fields = ExtractHiddenFields(startHtml);

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<RobokassaPaymentOptions>();
        var resultFields = new Dictionary<string, string>
        {
            ["OutSum"] = fields["OutSum"],
            ["InvId"] = fields["InvId"],
            ["Shp_mailingId"] = fields["Shp_mailingId"]
        };
        resultFields["SignatureValue"] = RobokassaPaymentModule.BuildResultSignature(resultFields["OutSum"], resultFields["InvId"], options.Password2, new Dictionary<string, string> { ["Shp_mailingId"] = resultFields["Shp_mailingId"] });

        var result = await client.PostAsync("/payments/robokassa/result", new FormUrlEncodedContent(resultFields));
        var body = await result.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal($"OK{fields["InvId"]}", body);
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(MailingStatus.Paid, mailing.Status);
    }

    [Fact]
    public async Task Robokassa_result_url_accepts_equivalent_six_decimal_amount()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Robokassa six decimal amount");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Transactional);
        var startHtml = await StartPayment(client, mailingId);
        var fields = ExtractHiddenFields(startHtml);

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<RobokassaPaymentOptions>();
        var outSum = decimal.Parse(fields["OutSum"], CultureInfo.InvariantCulture).ToString("0.000000", CultureInfo.InvariantCulture);
        var resultFields = new Dictionary<string, string>
        {
            ["OutSum"] = outSum,
            ["InvId"] = fields["InvId"],
            ["Shp_mailingId"] = fields["Shp_mailingId"]
        };
        resultFields["SignatureValue"] = RobokassaPaymentModule.BuildResultSignature(resultFields["OutSum"], resultFields["InvId"], options.Password2, new Dictionary<string, string> { ["Shp_mailingId"] = resultFields["Shp_mailingId"] });

        var result = await client.PostAsync("/payments/robokassa/result", new FormUrlEncodedContent(resultFields));
        var body = await result.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal($"OK{fields["InvId"]}", body);
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(MailingStatus.Paid, mailing.Status);
    }

    [Fact]
    public async Task Robokassa_success_url_pending_payment_does_not_loop_back_to_payment()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Robokassa pending success");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Transactional);
        var startHtml = await StartPayment(client, mailingId);
        var fields = ExtractHiddenFields(startHtml);
        var successFields = BuildSuccessCallbackFields(factory, fields);

        var success = await client.PostAsync("/payments/robokassa/success", new FormUrlEncodedContent(successFields));
        var successHtml = await success.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Contains("Переход после оплаты получен", successHtml);
        Assert.Contains("ждём серверное подтверждение оплаты", successHtml);
        Assert.Contains($"href='/mailings/{mailingId}/send'", successHtml);
        Assert.Contains("В личный кабинет", successHtml);
        Assert.DoesNotContain("Открыть страницу оплаты", successHtml);
        Assert.DoesNotContain($"href='/mailings/{mailingId}/payment'", successHtml);
        Assert.DoesNotContain($"href='/mailings/{mailingId}/checks'", successHtml);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(MailingStatus.PaymentPending, mailing.Status);
    }

    [Fact]
    public async Task Robokassa_success_url_confirmed_payment_redirects_to_checks_and_starts_review()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Robokassa confirmed success");
        using var client = CreateAuthenticatedClient(factory, allowAutoRedirect: false);
        await Prepare(client, mailingId, MessageType.Transactional);
        var startHtml = await StartPayment(client, mailingId);
        var fields = ExtractHiddenFields(startHtml);
        var resultFields = BuildResultCallbackFields(factory, fields);

        var result = await client.PostAsync("/payments/robokassa/result", new FormUrlEncodedContent(resultFields));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var successFields = BuildSuccessCallbackFields(factory, fields);
        var success = await client.PostAsync("/payments/robokassa/success", new FormUrlEncodedContent(successFields));

        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/send", success.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.True(
            mailing.Status is MailingStatus.Approved or MailingStatus.ReviewRequired,
            $"Expected checks to start after payment, actual status: {mailing.Status}");
    }

    [Fact]
    public async Task Fake_robokassa_checkout_can_complete_successful_payment()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Fake Robokassa checkout");
        using var client = CreateAuthenticatedClient(factory, allowAutoRedirect: false);
        await Prepare(client, mailingId, MessageType.Transactional);
        var startHtml = await StartPayment(client, mailingId);
        var fields = ExtractHiddenFields(startHtml);

        var checkout = await client.PostAsync("/payments/robokassa/fake/checkout", new FormUrlEncodedContent(fields));
        var checkoutHtml = await checkout.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        Assert.Contains("Тестовый модуль Robokassa", checkoutHtml);
        Assert.Contains("Оплатить успешно", checkoutHtml);

        var success = await client.PostAsync("/payments/robokassa/fake/success", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/send", success.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.True(
            mailing.Status is MailingStatus.Approved or MailingStatus.ReviewRequired,
            $"Expected checks to start after payment, actual status: {mailing.Status}");
    }

    [Fact]
    public async Task Robokassa_result_url_rejects_invalid_signature()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Robokassa bad signature");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Transactional);
        var startHtml = await StartPayment(client, mailingId);
        var fields = ExtractHiddenFields(startHtml);

        var resultFields = new Dictionary<string, string>
        {
            ["OutSum"] = fields["OutSum"],
            ["InvId"] = fields["InvId"],
            ["Shp_mailingId"] = fields["Shp_mailingId"],
            ["SignatureValue"] = "BAD"
        };
        var result = await client.PostAsync("/payments/robokassa/result", new FormUrlEncodedContent(resultFields));

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(MailingStatus.PaymentPending, mailing.Status);
    }

    [Fact]
    public async Task Promo_message_with_saved_advertising_consent_can_start_payment()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Promo payment start");
        using var client = CreateAuthenticatedClient(factory);
        await Prepare(client, mailingId, MessageType.Advertising);
        var paymentStartPath = $"/mailings/{mailingId}/payment/" + "fake-start";

        var ok = await client.PostAsync(paymentStartPath, PaymentConfirmations());
        var html = await ok.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Оплата через Robokassa", html);
        Assert.DoesNotContain("Нужно подтвердить рекламное согласие", html);
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
        ["campaignLaunchConfirmation"] = "on"
    });

    private static async Task<string> StartPayment(HttpClient client, Guid mailingId)
    {
        var response = await client.PostAsync($"/mailings/{mailingId}/payment/fake-start", PaymentConfirmations());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Оплата через Robokassa", html);
        return html;
    }

    private static Dictionary<string, string> ExtractHiddenFields(string html) =>
        HiddenInputRegex.Matches(html).ToDictionary(
            match => WebUtility.HtmlDecode(match.Groups["name"].Value),
            match => WebUtility.HtmlDecode(match.Groups["value"].Value),
            StringComparer.Ordinal);

    private static Dictionary<string, string> BuildResultCallbackFields(WebApplicationFactory<Program> factory, IReadOnlyDictionary<string, string> startFields)
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<RobokassaPaymentOptions>();
        var shp = new Dictionary<string, string> { ["Shp_mailingId"] = startFields["Shp_mailingId"] };
        var fields = new Dictionary<string, string>
        {
            ["OutSum"] = startFields["OutSum"],
            ["InvId"] = startFields["InvId"],
            ["Shp_mailingId"] = startFields["Shp_mailingId"]
        };
        fields["SignatureValue"] = RobokassaPaymentModule.BuildResultSignature(fields["OutSum"], fields["InvId"], options.Password2, shp);
        return fields;
    }

    private static Dictionary<string, string> BuildSuccessCallbackFields(WebApplicationFactory<Program> factory, IReadOnlyDictionary<string, string> startFields)
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<RobokassaPaymentOptions>();
        var shp = new Dictionary<string, string> { ["Shp_mailingId"] = startFields["Shp_mailingId"] };
        var fields = new Dictionary<string, string>
        {
            ["OutSum"] = startFields["OutSum"],
            ["InvId"] = startFields["InvId"],
            ["Shp_mailingId"] = startFields["Shp_mailingId"]
        };
        fields["SignatureValue"] = RobokassaPaymentModule.BuildSuccessSignature(fields["OutSum"], fields["InvId"], options.Password1, shp);
        return fields;
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

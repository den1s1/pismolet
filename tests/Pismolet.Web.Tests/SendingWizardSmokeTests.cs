using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
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

public sealed class SendingWizardSmokeTests
{
    private const string OwnerEmail = "sending-smoke@example.test";

    [Fact]
    public async Task Approved_mailing_can_open_launch_screen_and_start_sending()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Launch smoke campaign");
        using var client = CreateAuthenticatedClient(factory);
        await PrepareAndApprove(factory, client, mailingId, "Launch smoke campaign");

        var launchHtml = await client.GetStringAsync($"/mailings/{mailingId}/send");
        Assert.Contains("Запуск рассылки", launchHtml);
        Assert.Contains("Запустить отправку", launchHtml);
        Assert.Contains("Писем в очереди", launchHtml);

        var response = await client.PostAsync($"/mailings/{mailingId}/send/start", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Рассылка запущена", html);
        Assert.Contains("Отправка идёт постепенно", html);
        Assert.Contains("Писем в очереди", html);
        Assert.Contains("Оплачено писем", html);
        Assert.Contains("Открыто сейчас", html);
        Assert.Contains("Открытий всего", html);
        Assert.Contains("Последнее открытие", html);
        Assert.Contains("Кликнувшие сейчас", html);
        Assert.Contains("Кликнувшие получатели", html);
        Assert.Contains("Кликов всего", html);
        Assert.Contains("Последнее нажатие", html);
        Assert.Contains("Переходы по ссылкам", html);
        Assert.Contains("Ответов сейчас", html);
        Assert.Contains("Скачать Excel-отчёт", html);
        Assert.DoesNotContain("Прочитано", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_report_can_be_exported_as_xlsx()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Excel report campaign");
        using var client = CreateAuthenticatedClient(factory);
        await PrepareAndApprove(factory, client, mailingId, "Excel report campaign");
        await client.PostAsync($"/mailings/{mailingId}/send/start", new FormUrlEncodedContent(new Dictionary<string, string>()));

        var response = await client.GetAsync($"/mailings/{mailingId}/send/export.xlsx");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var workbookText = ReadWorkbookText(bytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.DispositionType ?? string.Empty);
        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        Assert.Contains("reader1@example.test", workbookText);
        Assert.Contains("reader2@example.test", workbookText);
        Assert.Contains("Ожидаем статус доставки", workbookText);
    }

    [Fact]
    public async Task Dashboard_history_shows_launched_mailing_status_cost_and_action()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "History launch campaign");
        using var client = CreateAuthenticatedClient(factory);
        await PrepareAndApprove(factory, client, mailingId, "History launch campaign");
        await client.PostAsync($"/mailings/{mailingId}/send/start", new FormUrlEncodedContent(new Dictionary<string, string>()));

        var html = await client.GetStringAsync("/dashboard");

        Assert.Contains("История рассылок", html);
        Assert.Contains("History launch campaign", html);
        Assert.Contains("Количество писем", html);
        Assert.Contains("Стоимость", html);
        Assert.Contains("Статус", html);
        Assert.Contains("Открыть отправку", html);
    }

    private static string ReadWorkbookText(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var builder = new StringBuilder();
        foreach (var entry in archive.Entries.Where(x => x.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) || string.Equals(x.FullName, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            builder.Append(reader.ReadToEnd());
        }

        return builder.ToString();
    }

    private static async Task PrepareAndApprove(WebApplicationFactory<Program> factory, HttpClient client, Guid mailingId, string subject)
    {
        await ImportAcceptedAddresses(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        await SaveMessage(client, mailingId, subject);

        using var scope = factory.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<IMailingPaymentService>();
        var paymentStart = payments.StartPayment(OwnerEmail, mailingId, Request());
        Assert.True(paymentStart.Ok, paymentStart.Error);
        var operationId = paymentStart.Review?.Payment?.Attempts.LastOrDefault()?.ProviderOperationId;
        Assert.False(string.IsNullOrWhiteSpace(operationId));

        var paid = payments.ConfirmPayment(OwnerEmail, mailingId, operationId!, Request());
        Assert.True(paid.Ok, paid.Error);

        var reviews = scope.ServiceProvider.GetRequiredService<IMailingReviewService>();
        var review = reviews.StartChecks(OwnerEmail, mailingId, Request());
        Assert.True(review.Ok, review.Error);
        Assert.NotNull(review.State);

        if (review.State.Mailing.Status == MailingStatus.Approved)
        {
            return;
        }

        Assert.NotNull(review.State.Review);
        var admin = scope.ServiceProvider.GetRequiredService<IModerationAdminService>();
        var approved = admin.Approve(review.State.Review.Id, "admin@example.test", "Approved in smoke test", Request());
        Assert.True(approved.Ok, approved.Error);
    }

    private static async Task ImportAcceptedAddresses(HttpClient client, Guid mailingId)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("reader1@example.test\nreader2@example.test"), "manualAddresses" }
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
        Assert.True(response.IsSuccessStatusCode, $"Unexpected declaration response: {(int)response.StatusCode}");
    }

    private static async Task SaveMessage(HttpClient client, Guid mailingId, string subject)
    {
        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Sender",
            ["subject"] = subject,
            ["body"] = "Hello from Pismolet."
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK, $"Unexpected message response: {(int)response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.Equal($"/mailings/{mailingId}/payment", response.Headers.Location?.OriginalString);
        }
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
        var result = accounts.Register(new RegisterUserCommand(OwnerEmail, "TestPassword123!", "Sending Smoke", "+79990000000"), Request());
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

    private static RequestMetadata Request() => new("127.0.0.1", "sending-wizard-smoke-tests");

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

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

public sealed class AdminPaymentEndpointsTests
{
    private const string AdminEmail = "admin-payments@example.test";
    private const string OwnerEmail = "payment-owner@example.test";

    [Fact]
    public async Task Admin_payments_page_shows_payment_stats_and_filters_by_status()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Payment Owner");
        SeedMailing(factory, OwnerEmail, "Paid campaign", MailingStatus.Paid);
        SeedMailing(factory, OwnerEmail, "Draft campaign", MailingStatus.Draft);
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync("/admin/payments?status=paid");

        Assert.Contains("Оплаты", html);
        Assert.Contains("Контроль счетов по кампаниям", html);
        Assert.Contains("Оплачено всего", html);
        Assert.Contains("Paid campaign", html);
        Assert.Contains("Оплачено", html);
        Assert.DoesNotContain("Draft campaign", html);
    }

    [Fact]
    public async Task Admin_payment_profile_shows_amount_client_and_actions()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Payment Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Profile payment", MailingStatus.PaymentPending);
        using var client = CreateAuthenticatedClient(factory, AdminEmail);

        var html = await client.GetStringAsync($"/admin/payments/{mailingId}");

        Assert.Contains("Профиль платежа", html);
        Assert.Contains("Profile payment", html);
        Assert.Contains(OwnerEmail, html);
        Assert.Contains("Ожидает оплаты", html);
        Assert.Contains("События оплаты", html);
        Assert.Contains("Запросить ручную сверку", html);
        Assert.Contains("Клиентский экран оплаты", html);
    }

    [Fact]
    public async Task Admin_payment_reconcile_action_redirects_back_to_payment_profile()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Payment Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Reconcile payment", MailingStatus.PaymentPending);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, AdminEmail);

        var response = await client.PostAsync($"/admin/payments/{mailingId}/reconcile", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/admin/payments/{mailingId}", response.Headers.Location?.ToString() ?? string.Empty);
        Assert.Contains("action=reconcile", response.Headers.Location?.ToString() ?? string.Empty);
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

    private static Guid SeedMailing(WebApplicationFactory<Program> factory, string ownerEmail, string subject, MailingStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var repository = scope.ServiceProvider.GetRequiredService<IMailingRepository>();
        var result = service.CreateDraft(new CreateMailingCommand(ownerEmail, subject), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);
        repository.Update(result.Mailing.WithStatus(status));
        return result.Mailing.Id;
    }

    private static RequestMetadata Request() => new("127.0.0.1", "admin-payment-endpoint-tests");

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

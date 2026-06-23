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
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class ImportWarningUiSmokeTests
{
    private const string OwnerEmail = "import-warning@example.test";
    private const string WarningEmail = "soft-warning@example.test";

    [Fact]
    public async Task SoftBounce_warning_is_shown_separately_from_excluded_addresses()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        SeedSoftBounces(factory);
        using var client = CreateAuthenticatedClient(factory);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(WarningEmail), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Предупреждения", html);
        Assert.Contains("Временные ошибки доставки ранее: 2", html);
        Assert.Contains("Адрес не исключён", html);
        Assert.Contains("Что исключено", html);
        Assert.Contains("Исключённых адресов нет.", html);
        Assert.True(html.IndexOf("Предупреждения", StringComparison.Ordinal) < html.IndexOf("Что исключено", StringComparison.Ordinal));
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
        var result = accounts.Register(new RegisterUserCommand(OwnerEmail, "Password123!", "Import Warning"), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static Guid SeedMailing(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(OwnerEmail, "Import warning campaign"), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);
        return result.Mailing.Id;
    }

    private static void SeedSoftBounces(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var sendEvents = scope.ServiceProvider.GetRequiredService<ISendEventRepository>();
        sendEvents.Save(SendEvent.Pending(Guid.NewGuid(), OwnerEmail, WarningEmail)
            .ApplyDeliveryStatus(DeliveryStatus.SoftBounce, DateTimeOffset.UtcNow.AddDays(-2), "temporary failure 1"));
        sendEvents.Save(SendEvent.Pending(Guid.NewGuid(), OwnerEmail, WarningEmail)
            .ApplyDeliveryStatus(DeliveryStatus.SoftBounce, DateTimeOffset.UtcNow.AddDays(-1), "temporary failure 2"));
    }

    private static RequestMetadata Request() => new("127.0.0.1", "import-warning-ui-smoke-tests");

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

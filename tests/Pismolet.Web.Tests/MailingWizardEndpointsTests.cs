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

namespace Pismolet.Web.Tests;

public sealed class MailingWizardEndpointsTests
{
    private const string OwnerEmail = "wizard-owner@example.test";

    [Fact]
    public async Task Authenticated_user_can_open_new_mailing_wizard()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);

        var html = await client.GetStringAsync("/mailings/new");

        Assert.Contains("Новая рассылка", html);
        Assert.Contains("Создать черновик", html);
        Assert.Contains("1. Адреса", html);
        Assert.Contains("wizard-steps", html);
    }

    [Fact]
    public async Task Authenticated_user_can_open_address_wizard_step()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Wizard campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/recipients");

        Assert.Contains("1. Добавьте список адресов", html);
        Assert.Contains("Не используйте купленные или чужие базы", html);
        Assert.Contains("name='manualAddresses'", html);
        Assert.Contains("dropzone", html);
        Assert.Contains("Проверить адреса", html);
    }

    [Fact]
    public async Task Manual_address_import_preserves_list_and_shows_import_result()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Manual import campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("first@example.test\nwrong-email\nFIRST@example.test"), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Адреса проверены", html);
        Assert.Contains("Принято к отправке", html);
        Assert.Contains("Дублей и ошибок", html);
        Assert.Contains("Ранее отписались", html);
        Assert.Contains("Перейти к следующему шагу", html);
        Assert.Contains($"/mailings/{mailingId}/declaration", html);
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

    private static RequestMetadata Request() => new("127.0.0.1", "mailing-wizard-endpoint-tests");

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

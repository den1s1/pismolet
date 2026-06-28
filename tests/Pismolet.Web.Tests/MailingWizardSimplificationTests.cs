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

namespace Pismolet.Web.Tests;

public sealed class MailingWizardSimplificationTests
{
    private const string UserEmail = "wizard-user@example.test";

    [Fact]
    public async Task New_mailing_goes_directly_to_message_step_without_draft_page()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, UserEmail, "Wizard User");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, UserEmail);

        var response = await client.GetAsync("/mailings/new");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.StartsWith("/mailings/", location, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/message", location, StringComparison.OrdinalIgnoreCase);

        var page = await client.GetStringAsync(location);
        Assert.Contains("1. Напишите письмо", page);
        Assert.Contains("2. Адресаты", page);
        Assert.Contains("3. Просмотр списка", page);
        Assert.Contains("4. Подтверждение", page);
        Assert.Contains("5. Оплата", page);
        Assert.Contains("Сохранить письмо и перейти к адресатам", page);
        Assert.DoesNotContain("Создайте черновик рассылки", page);
        Assert.DoesNotContain("Название рассылки", page);
        Assert.DoesNotContain(">Черновик<", page);
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

    private static void SeedUser(WebApplicationFactory<Program> factory, string email, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var result = accounts.Register(new RegisterUserCommand(email, "Password123!", displayName, "+79990000000"), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static RequestMetadata Request() => new("127.0.0.1", "mailing-wizard-tests");

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

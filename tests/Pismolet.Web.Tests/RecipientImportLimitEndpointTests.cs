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

public sealed class RecipientImportLimitEndpointTests
{
    private const string OwnerEmail = "recipient-limit@example.test";

    [Fact]
    public async Task Recipient_import_rejects_oversized_file_before_copying_to_memory()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        using var fileContent = new ByteArrayContent(new byte[1024 * 1024 + 1]);
        using var content = new MultipartFormDataContent
        {
            { fileContent, "file", "too-big.csv" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Файл слишком большой", html);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Empty(mailing.Recipients);
    }

    [Fact]
    public async Task Recipient_import_checks_manual_size_by_utf8_bytes()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(new string('Ж', 600_000)), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ручная вставка слишком большая", html);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Empty(mailing.Recipients);
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
        var result = accounts.Register(new RegisterUserCommand(OwnerEmail, "Password123!", "Recipient Limit", "+79990000000"), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static Guid SeedMailing(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(OwnerEmail, "Recipient limit campaign"), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);
        return result.Mailing.Id;
    }

    private static RequestMetadata Request() => new("127.0.0.1", "recipient-import-limit-tests");

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

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

namespace Pismolet.Web.Tests;

public sealed class LegalUiLinksTests
{
    private const string OwnerEmail = "legal-links@example.test";
    private static readonly Regex LegalHrefRegex = new("href='(?<href>/legal/[^']+)'", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public async Task Core_flow_legal_links_resolve_and_return_to_origin()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory, "Legal links campaign");
        using var client = CreateAuthenticatedClient(factory);

        var pages = new List<string>
        {
            await factory.CreateClient().GetStringAsync("/account/register"),
            await client.GetStringAsync($"/mailings/{mailingId}/recipients")
        };

        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        pages.Add(await client.GetStringAsync($"/mailings/{mailingId}/message"));

        await SaveMessage(client, mailingId);
        pages.Add(await client.GetStringAsync($"/mailings/{mailingId}/payment"));

        var hrefs = pages.SelectMany(ExtractLegalHrefs).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray();

        Assert.Contains("/legal/offer?returnUrl=/account/register", hrefs);
        Assert.Contains("/legal/privacy?returnUrl=/account/register", hrefs);
        Assert.Contains($"/legal/anti-spam?returnUrl=/mailings/{mailingId}/recipients", hrefs);
        Assert.Contains($"/legal/data-processing?returnUrl=/mailings/{mailingId}/recipients", hrefs);
        Assert.Contains($"/legal/base-lawfulness?returnUrl=/mailings/{mailingId}/recipients", hrefs);
        Assert.Contains($"/legal/prohibited-content?returnUrl=/mailings/{mailingId}/message", hrefs);
        Assert.Contains($"/legal/service-email-footer?returnUrl=/mailings/{mailingId}/message", hrefs);
        Assert.Contains($"/legal/payment-and-refund?returnUrl=/mailings/{mailingId}/payment", hrefs);
        Assert.DoesNotContain(hrefs, href => href.StartsWith("https://pismolet.ru/legal", StringComparison.OrdinalIgnoreCase));

        foreach (var href in hrefs)
        {
            var response = await client.GetAsync(href);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var returnUrl = ReturnUrl(href);
            if (returnUrl is not null)
            {
                Assert.Contains($"href='{returnUrl}'", html);
            }
        }
    }

    private static IReadOnlyCollection<string> ExtractLegalHrefs(string html) =>
        LegalHrefRegex.Matches(html).Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value)).ToArray();

    private static string? ReturnUrl(string href)
    {
        const string marker = "?returnUrl=";
        var index = href.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? null : WebUtility.UrlDecode(href[(index + marker.Length)..]);
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

        var response = await client.PostAsync($"/mailings/{mailingId}/confirmation", declarationForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected declaration response: {(int)response.StatusCode}");
    }

    private static async Task SaveMessage(HttpClient client, Guid mailingId)
    {
        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Sender",
            ["subject"] = "Subject",
            ["body"] = "Body"
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK, $"Unexpected message response: {(int)response.StatusCode}");
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
        var result = accounts.Register(new RegisterUserCommand(OwnerEmail, "PassForTests2026!", "Legal Links", "+79990000000"), Request());
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

    private static RequestMetadata Request() => new("127.0.0.1", "legal-ui-links-tests");

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

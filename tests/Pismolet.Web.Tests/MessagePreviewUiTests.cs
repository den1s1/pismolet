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

public sealed class MessagePreviewUiTests
{
    private const string OwnerEmail = "message-preview@example.test";

    [Fact]
    public async Task Message_preview_is_opened_on_separate_page_and_keeps_service_footer_collapsed()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        await SaveMessage(client, mailingId);

        var editorHtml = await client.GetStringAsync($"/mailings/{mailingId}/message");
        Assert.Contains("Предпросмотр", editorHtml);
        Assert.DoesNotContain("<div class='mail-preview-body'>", editorHtml);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/message/preview");

        var previewStart = html.IndexOf("<div class='mail-preview-body'>", StringComparison.Ordinal);
        var detailsStart = html.IndexOf("<details class='service-preview-details'>", StringComparison.Ordinal);
        Assert.True(previewStart >= 0, "Main preview body was not found.");
        Assert.True(detailsStart > previewStart, "Collapsed service preview was not found after the main preview body.");

        var mainPreview = html[previewStart..detailsStart];
        Assert.Contains("Приглашаем на встречу", mainPreview);
        Assert.Contains("Здравствуйте!", mainPreview);
        Assert.Contains("Письмолёт автоматически добавит причину получения, отписку и служебный номер.", mainPreview);
        Assert.DoesNotContain("/unsubscribe/example-token", mainPreview);
        Assert.DoesNotContain("Служебный идентификатор рассылки", mainPreview);

        var collapsedPreview = html[detailsStart..];
        Assert.Contains("Показать служебный блок", collapsedPreview);
        Assert.Contains("/unsubscribe/example-token", collapsedPreview);
        Assert.Contains("Служебный идентификатор рассылки", collapsedPreview);
    }

    [Fact]
    public void Production_message_rendering_still_includes_unsubscribe_and_service_identifier()
    {
        var mailing = Mailing.Draft(OwnerEmail, "Production footer campaign")
            .WithMessageDraft(MailingMessageDraft.Create(
                "Библиотека №5",
                "Приглашаем на встречу",
                "Здравствуйте!",
                MessageType.Transactional,
                DateTimeOffset.UtcNow));
        var preview = new MessageRenderingService().RenderPreview(mailing);

        Assert.Contains("/unsubscribe/example-token", preview.UnsubscribeUrl);
        Assert.Contains("Служебный идентификатор рассылки", preview.ServiceIdentifier);
        Assert.Contains(preview.UnsubscribeUrl, preview.PlainText);
        Assert.Contains(preview.ServiceIdentifier, preview.PlainText);
    }

    [Fact]
    public async Task Html_message_preview_uses_saved_explicit_html_format()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        await SaveMessage(
            client,
            mailingId,
            "html",
            "Plain body should not be saved",
            "<h1>Акция</h1><p>HTML body</p>");

        var html = await client.GetStringAsync($"/mailings/{mailingId}/message/preview");

        Assert.Contains("HTML-предпросмотр письма", html);
        Assert.Contains("&lt;h1&gt;Акция&lt;/h1&gt;", html);
        Assert.DoesNotContain("Plain body should not be saved", html);
        var mailing = GetMailing(factory, mailingId);
        Assert.Equal(MessageBodyFormat.Html, mailing.MessageDraft?.BodyFormat);
        Assert.Equal("<h1>Акция</h1><p>HTML body</p>", mailing.MessageDraft?.Body);
    }

    [Fact]
    public async Task Text_message_preview_keeps_html_like_plain_text_when_text_format_is_selected()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        await SaveMessage(
            client,
            mailingId,
            "text",
            "Покажите строку <p data-marker=\"plain\"> как текст.",
            "<h1>Wrong HTML body</h1>");

        var html = await client.GetStringAsync($"/mailings/{mailingId}/message/preview");

        Assert.DoesNotContain("HTML-предпросмотр письма", html);
        Assert.Contains("&lt;p data-marker=&quot;plain&quot;&gt;", html);
        Assert.DoesNotContain("Wrong HTML body", html);
        var mailing = GetMailing(factory, mailingId);
        Assert.Equal(MessageBodyFormat.Text, mailing.MessageDraft?.BodyFormat);
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

    private static async Task SaveMessage(HttpClient client, Guid mailingId, string bodyFormat, string plainBody, string htmlBody)
    {
        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "Приглашаем на встречу",
            ["bodyFormat"] = bodyFormat,
            ["plainBody"] = plainBody,
            ["htmlBody"] = htmlBody
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected message response: {(int)response.StatusCode}");
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
        var result = accounts.Register(new RegisterUserCommand(OwnerEmail, "PassForTests2026!", "Message Preview", "+79990000000"), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static Guid SeedMailing(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(OwnerEmail, "Message preview campaign"), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);
        return result.Mailing.Id;
    }

    private static Mailing GetMailing(WebApplicationFactory<Program> factory, Guid mailingId)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        return mailing;
    }

    private static RequestMetadata Request() => new("127.0.0.1", "message-preview-ui-tests");

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

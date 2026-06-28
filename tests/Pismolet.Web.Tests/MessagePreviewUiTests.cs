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

    [Fact]
    public async Task Message_editor_exposes_minimal_rich_text_toolbar_for_regular_body_and_raw_html_textarea()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/message");

        var visualPanelStart = html.IndexOf("data-body-panel='visual'", StringComparison.Ordinal);
        var richEditorStart = html.IndexOf("data-rich-text-editor", StringComparison.Ordinal);
        var htmlPanelStart = html.IndexOf("data-body-panel='html'", StringComparison.Ordinal);
        Assert.True(visualPanelStart >= 0, "Regular message panel was not found.");
        Assert.True(htmlPanelStart > visualPanelStart, "HTML panel was not found after regular message panel.");
        Assert.True(richEditorStart > visualPanelStart && richEditorStart < htmlPanelStart, "Rich editor must belong to the regular message tab.");
        Assert.Contains("data-rich-command='bold'", html);
        Assert.Contains("data-rich-command='italic'", html);
        Assert.Contains("data-rich-font-size", html);
        Assert.Contains("data-rich-color", html);
        Assert.Contains("data-rich-link-input", html);
        Assert.Contains("name='visualBody'", html);
        Assert.Contains("name='htmlBody'", html);
        Assert.Contains("data-body-fallback", html);
        Assert.DoesNotContain("disabled = tab", html);
        var htmlPanelEnd = html.IndexOf("</section>", htmlPanelStart, StringComparison.Ordinal);
        Assert.True(htmlPanelEnd > htmlPanelStart, "HTML message panel closing section was not found.");
        var htmlPanel = html[htmlPanelStart..htmlPanelEnd];
        Assert.Contains("<textarea name='htmlBody'", htmlPanel);
        Assert.DoesNotContain("data-rich-text-editor", htmlPanel);
    }

    [Fact]
    public async Task Html_message_save_keeps_simple_rich_formatting()
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
            "<p><strong>Важное</strong> <em>сообщение</em> <span style=\"color:#3366ff;font-size:18px\">синим</span> <a href=\"https://example.ru/news\">читать</a></p>");

        var mailing = GetMailing(factory, mailingId);
        Assert.Equal(MessageBodyFormat.Html, mailing.MessageDraft?.BodyFormat);
        Assert.Equal("<p><strong>Важное</strong> <em>сообщение</em> <span style=\"color:#3366ff;font-size:18px\">синим</span> <a href=\"https://example.ru/news\">читать</a></p>", mailing.MessageDraft?.Body);
    }

    [Fact]
    public async Task Regular_visual_message_save_blocks_dangerous_html()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);

        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "Приглашаем на встречу",
            ["bodyTab"] = "visual",
            ["bodyFormat"] = "html",
            ["visualBody"] = "<p onclick=\"alert(1)\"><strong>Привет</strong><script>alert('x')</script></p><a href=\"javascript:alert(1)\">плохая ссылка</a><a href=\"https://example.ru\">хорошая ссылка</a>",
            ["htmlBody"] = "<h1>Raw HTML body should not be saved</h1>"
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("HTML содержит запрещённый обработчик события onclick", html);
        Assert.Null(GetMailing(factory, mailingId).MessageDraft);
    }

    [Fact]
    public async Task Html_message_save_uses_raw_html_when_visual_tab_marker_is_stale()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);

        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "HTML письмо",
            ["bodyTab"] = "visual",
            ["bodyFormat"] = "html",
            ["visualBody"] = string.Empty,
            ["htmlBody"] = "<h1>Готовый HTML</h1><p>Текст письма</p>"
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected message response: {(int)response.StatusCode}");

        var mailing = GetMailing(factory, mailingId);
        Assert.Equal(MessageBodyFormat.Html, mailing.MessageDraft?.BodyFormat);
        Assert.Equal("<h1>Готовый HTML</h1><p>Текст письма</p>", mailing.MessageDraft?.Body);
    }

    [Fact]
    public async Task Html_message_save_uses_legacy_body_fallback_when_html_field_is_missing()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);

        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "HTML письмо",
            ["bodyTab"] = "html",
            ["bodyFormat"] = "html",
            ["visualBody"] = string.Empty,
            ["body"] = "<h1>Fallback HTML</h1><p>Текст письма</p>"
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected message response: {(int)response.StatusCode}");

        var mailing = GetMailing(factory, mailingId);
        Assert.Equal(MessageBodyFormat.Html, mailing.MessageDraft?.BodyFormat);
        Assert.Equal("<h1>Fallback HTML</h1><p>Текст письма</p>", mailing.MessageDraft?.Body);
    }

    [Fact]
    public async Task Html_message_save_keeps_common_email_html_without_stripping()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        var emailHtml = """
<table width="600" cellpadding="0" cellspacing="0">
  <tr><td style="color:#333333;font-size:18px"><h1>Готовый HTML</h1><p>Текст письма</p><img src="https://example.ru/pic.png" width="120" alt="Картинка"></td></tr>
</table>
""";

        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "HTML письмо",
            ["bodyTab"] = "html",
            ["bodyFormat"] = "html",
            ["htmlBody"] = emailHtml
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected message response: {(int)response.StatusCode}");

        var mailing = GetMailing(factory, mailingId);
        Assert.Equal(MessageBodyFormat.Html, mailing.MessageDraft?.BodyFormat);
        Assert.Equal(emailHtml, mailing.MessageDraft?.Body);
    }

    [Fact]
    public async Task Message_save_error_keeps_submitted_sender_subject_and_html_body()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);

        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Отправитель после ошибки",
            ["subject"] = "Тема после ошибки",
            ["bodyTab"] = "html",
            ["bodyFormat"] = "html",
            ["visualBody"] = string.Empty,
            ["htmlBody"] = string.Empty
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Напишите текст письма.", html);
        Assert.Contains("value='Отправитель после ошибки'", html);
        Assert.Contains("value='Тема после ошибки'", html);
        var htmlPanelStart = html.IndexOf("data-body-panel='html'", StringComparison.Ordinal);
        Assert.True(htmlPanelStart >= 0, "HTML panel was not found.");
        var htmlPanelEnd = html.IndexOf("</section>", htmlPanelStart, StringComparison.Ordinal);
        Assert.True(htmlPanelEnd > htmlPanelStart, "HTML panel closing section was not found.");
        Assert.DoesNotContain("data-body-panel='html' style='display:none'", html[htmlPanelStart..htmlPanelEnd]);
    }

    [Fact]
    public async Task Html_message_save_blocks_dangerous_markup_before_persisting()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory);
        var mailingId = SeedMailing(factory);
        using var client = CreateAuthenticatedClient(factory);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);

        var dangerousHtml = """
<p onclick="alert(1)">Здравствуйте<script>alert('x')</script></p>
<a href="javascript:alert(1)">плохая ссылка</a>
<a href="https://example.ru">хорошая ссылка</a>
<span style="color:#00ff00;font-size:18px;background-image:url(javascript:evil)">опасный стиль</span>
<style>body{display:none}</style>
""";
        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "Опасный HTML",
            ["bodyTab"] = "html",
            ["bodyFormat"] = "html",
            ["htmlBody"] = dangerousHtml
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("HTML содержит запрещённый обработчик события onclick", html);
        Assert.Contains("value='Библиотека №5'", html);
        Assert.Contains("value='Опасный HTML'", html);
        Assert.Contains("onclick=&quot;alert(1)&quot;", html);
        Assert.Null(GetMailing(factory, mailingId).MessageDraft);
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

using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MessageEditorServerFlowTests
{
    private static readonly RequestMetadata Request = new("127.0.0.1", "message-editor-tests");

    [Fact]
    public void Save_linkifies_plain_url_once_and_keeps_existing_anchor_unchanged()
    {
        var repository = new InMemoryMailingRepository();
        var mailing = Mailing.Draft("client@example.test", "Новости");
        repository.TryAdd(mailing);
        var service = new MailingMessageService(repository, new EmailNormalizer(), new InMemoryAuditLogger());
        const string body = "<p>Сайт https://example.org/news, <a href=\"https://example.org/ready\">готовая ссылка</a></p>";

        var first = service.Save(Command(mailing.Id, body));
        var second = service.Save(Command(mailing.Id, first.Mailing!.MessageDraft!.Body));

        const string expected = "<p>Сайт <a href=\"https://example.org/news\">https://example.org/news</a>, <a href=\"https://example.org/ready\">готовая ссылка</a></p>";
        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        Assert.Equal(expected, first.Mailing?.MessageDraft?.Body);
        Assert.Equal(expected, second.Mailing?.MessageDraft?.Body);
        Assert.Equal(2, CountOccurrences(second.Mailing!.MessageDraft!.Body, "<a href="));
    }

    [Fact]
    public void Reopen_and_preview_use_the_same_linkified_user_html()
    {
        const string body = "<p>Сайт https://example.org/news.</p>";
        const string linkified = "<p>Сайт <a href=\"https://example.org/news\">https://example.org/news</a>.</p>";

        var editorHtml = InvokePrivate<string>(
            "ToVisualEditorHtml",
            new[] { typeof(string), typeof(MessageBodyFormat) },
            body,
            MessageBodyFormat.Html);
        var previewHtml = InvokePrivate<string>(
            "HtmlBodyPreview",
            new[] { typeof(string), typeof(string), typeof(string), typeof(string) },
            body,
            "Причина получения письма.",
            "/unsubscribe/example-token",
            "Служебный идентификатор рассылки: TEST");

        Assert.Equal(linkified, editorHtml);
        Assert.Contains("&lt;a href=&quot;https://example.org/news&quot;&gt;https://example.org/news&lt;/a&gt;.", previewHtml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(editorHtml, "<a href="));
        Assert.Contains("data-pismolet-service-footer", previewHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_contains_exactly_ten_colors_link_popover_and_clear_formatting_command()
    {
        var mailing = Mailing.Draft("client@example.test", "Новости") with
        {
            MessageDraft = MailingMessageDraft.Create(
                "Библиотека №5",
                "Новости",
                "<p><span style=\"color:#b91c1c\">Красный текст</span></p>",
                MessageType.Transactional,
                DateTimeOffset.UtcNow,
                bodyFormat: MessageBodyFormat.Html),
            RecipientReason = "Вы зарегистрировались на встречу."
        };

        var html = InvokeMessageForm(mailing);

        Assert.Equal(10, CountOccurrences(html, "data-rich-color-value="));
        Assert.DoesNotContain("type='color'", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"color\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-rich-color-current", html, StringComparison.Ordinal);
        Assert.Contains("Цвет по умолчанию", html, StringComparison.Ordinal);
        Assert.Contains("data-rich-link-toggle", html, StringComparison.Ordinal);
        Assert.Contains("<h3 class='rich-popover-title' id='rich-link-title'>Ссылка</h3>", html, StringComparison.Ordinal);
        Assert.Contains("placeholder='https://'", html, StringComparison.Ordinal);
        Assert.Contains("data-rich-link-save", html, StringComparison.Ordinal);
        Assert.Contains("data-rich-link-cancel", html, StringComparison.Ordinal);
        Assert.Contains("data-rich-link-delete", html, StringComparison.Ordinal);
        Assert.Contains("Очистить форматирование", html, StringComparison.Ordinal);
        Assert.Contains("/message-editor.css", html, StringComparison.Ordinal);
        Assert.Contains("/message-editor.js", html, StringComparison.Ordinal);
        Assert.Contains("color:#b91c1c", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editor_script_contains_validation_focus_link_editing_and_format_cleanup_logic()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/message-editor.js");

        Assert.Contains("normalizeLinkUrl", script, StringComparison.Ordinal);
        Assert.Contains("https://", script, StringComparison.Ordinal);
        Assert.Contains("Разрешены только ссылки", script, StringComparison.Ordinal);
        Assert.Contains("linkInput.focus()", script, StringComparison.Ordinal);
        Assert.Contains("createLink", script, StringComparison.Ordinal);
        Assert.Contains("deleteLink", script, StringComparison.Ordinal);
        Assert.Contains("clearSelectedFormatting", script, StringComparison.Ordinal);
        Assert.Contains("preservedTags", script, StringComparison.Ordinal);
        Assert.Contains("element.removeAttribute(attribute.name)", script, StringComparison.Ordinal);
        Assert.Contains("preserveBold", script, StringComparison.Ordinal);
        Assert.Contains("preserveItalic", script, StringComparison.Ordinal);
        Assert.Contains("preserveUnderline", script, StringComparison.Ordinal);
        Assert.Contains("isServiceFooterElement", script, StringComparison.Ordinal);
    }

    private static SaveMailingMessageCommand Command(Guid mailingId, string body) => new(
        "client@example.test",
        mailingId,
        "Библиотека №5",
        "Новости",
        body,
        MessageType.Transactional,
        Request,
        BodyFormat: MessageBodyFormat.Html,
        RecipientReason: "Вы зарегистрировались на встречу.");

    private static string InvokeMessageForm(Mailing mailing)
    {
        var method = typeof(MailingRichMessageFlowEndpoints).GetMethod("MessageForm", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, new object?[]
        {
            mailing,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        })!;
    }

    private static T InvokePrivate<T>(string methodName, Type[] parameterTypes, params object?[] arguments)
    {
        var method = typeof(MailingRichMessageFlowEndpoints).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null)!;
        return (T)method.Invoke(null, arguments)!;
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}

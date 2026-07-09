using System.Reflection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailingServiceEmailFooterTests
{
    [Fact]
    public void Plain_text_footer_is_separated_and_does_not_expose_service_identifier()
    {
        const string unsubscribeUrl = "https://app.pismolet.ru/unsubscribe/test-token";

        var body = MailingServiceEmailFooter.PlainText(
            "Основной текст письма.",
            "Тестовый отправитель",
            unsubscribeUrl,
            "PL-TEST123");

        Assert.Contains("Основной текст письма.\n\nВы получили это письмо от Тестовый отправитель через Письмолёт", body, StringComparison.Ordinal);
        Assert.Contains($"\n\nОтписаться от писем через Письмолёт:\n{unsubscribeUrl}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Служебный идентификатор рассылки", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PL-TEST123", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_footer_uses_compact_link_and_separate_visual_block()
    {
        const string unsubscribeUrl = "https://app.pismolet.ru/unsubscribe/test-token";
        var plainText = MailingServiceEmailFooter.PlainText(
            "Основной текст письма.",
            "Тестовый отправитель",
            unsubscribeUrl,
            "PL-TEST123");
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildHtmlBody", BindingFlags.NonPublic | BindingFlags.Static);

        var html = Assert.IsType<string>(method!.Invoke(null, new object?[] { plainText, unsubscribeUrl, null, null }));

        Assert.Contains("border-top:1px solid #dbe4ef", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{unsubscribeUrl}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Отписаться от писем через Письмолёт</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain($">{unsubscribeUrl}<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Служебный идентификатор рассылки", html, StringComparison.Ordinal);
    }
}

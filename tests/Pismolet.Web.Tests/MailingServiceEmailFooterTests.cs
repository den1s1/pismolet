using System.Reflection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailingServiceEmailFooterTests
{
    [Fact]
    public void Internal_footer_keeps_service_identifier_for_preview_and_diagnostics()
    {
        const string unsubscribeUrl = "https://app.pismolet.ru/unsubscribe/test-token";
        const string serviceIdentifier = "Служебный идентификатор рассылки: PL-TEST123";

        var body = MailingServiceEmailFooter.PlainText(
            "Основной текст письма.",
            "Тестовый отправитель",
            unsubscribeUrl,
            serviceIdentifier);

        Assert.Contains("Основной текст письма.\n\nВы получили это письмо от Тестовый отправитель через Письмолёт", body, StringComparison.Ordinal);
        Assert.Contains($"\n\nОтписаться от всех рассылок через сервис: {unsubscribeUrl}", body, StringComparison.Ordinal);
        Assert.Contains(serviceIdentifier, body, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_footer_uses_compact_link_and_hides_service_identifier()
    {
        const string unsubscribeUrl = "https://app.pismolet.ru/unsubscribe/test-token";
        var plainText = MailingServiceEmailFooter.PlainText(
            "Основной текст письма.",
            "Тестовый отправитель",
            unsubscribeUrl,
            "Служебный идентификатор рассылки: PL-TEST123");
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildHtmlBody", BindingFlags.NonPublic | BindingFlags.Static);

        var html = Assert.IsType<string>(method!.Invoke(null, new object?[] { plainText, unsubscribeUrl, null, null }));

        Assert.Contains("border-top:1px solid #dbe4ef", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{unsubscribeUrl}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Отписаться от писем через Письмолёт</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain($">{unsubscribeUrl}<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Служебный идентификатор рассылки", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PL-TEST123", html, StringComparison.Ordinal);
    }
}

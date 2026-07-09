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

    [Fact]
    public void Mailru_postmaster_message_type_uses_only_letters_and_digits_and_is_at_most_32_chars()
    {
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildPostmasterMessageType", BindingFlags.NonPublic | BindingFlags.Static);

        var messageType = Assert.IsType<string>(method!.Invoke(null, new object?[] { " 64138d5d-0123-4567-89ab-cdef01234567-extra " }));

        Assert.Equal("64138d5d0123456789abcdef01234567", messageType);
        Assert.Equal(32, messageType.Length);
        Assert.All(messageType, ch => Assert.True(char.IsAsciiLetterOrDigit(ch)));
    }
}

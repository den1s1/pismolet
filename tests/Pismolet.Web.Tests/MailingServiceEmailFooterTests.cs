using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Postmaster;
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
    public void Mailru_postmaster_message_type_is_canonical_guid_without_separators()
    {
        var mailingId = Guid.Parse("64138d5d-0123-4567-89ab-cdef01234567");

        var messageType = MailruPostmasterMessageType.Build(mailingId);

        Assert.Equal("64138d5d0123456789abcdef01234567", messageType);
        Assert.Equal(32, messageType.Length);
        Assert.All(messageType, ch => Assert.True(char.IsAsciiLetterOrDigit(ch)));
    }

    [Fact]
    public void Mailing_mime_message_has_bulk_precedence_and_postmaster_headers()
    {
        var mailingId = Guid.Parse("87462f9c-ae73-4d90-aba8-69b980f49452");
        var adapter = new SmtpEmailProviderAdapter(
            new SmtpEmailProviderOptions(
                Host: "127.0.0.1",
                Port: 25,
                Username: string.Empty,
                Password: string.Empty,
                FromEmail: "info@pismolet.ru",
                FromName: "Письмолёт",
                SecureSocketOptions: "None",
                TimeoutSeconds: 30),
            new PublicUrlOptions("https://app.pismolet.ru"),
            NullLogger<SmtpEmailProviderAdapter>.Instance);
        var message = new EmailMessage(
            MailingId: mailingId,
            Recipient: new EmailRecipient("recipient@mail.ru"),
            SenderName: "Тестовый отправитель",
            Subject: "Тестовая рассылка",
            PlainTextBody: "Текст письма",
            UnsubscribeUrl: "https://app.pismolet.ru/unsubscribe/test-token",
            ServiceIdentifier: "PL-TEST",
            ReplyToAddress: "reply@reply.pismolet.ru",
            ReplyToken: "reply-token",
            Metadata: new Dictionary<string, string>
            {
                ["mailingId"] = mailingId.ToString("N"),
                ["recipientKey"] = "recipient-key"
            });
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildMimeMessage", BindingFlags.NonPublic | BindingFlags.Instance);

        var mime = Assert.IsType<MimeMessage>(method!.Invoke(adapter, new object?[] { message }));

        Assert.Equal("bulk", mime.Headers["Precedence"]);
        Assert.Equal(mailingId.ToString("N"), mime.Headers["X-Pismolet-Mailing-Id"]);
        Assert.Equal(MailruPostmasterMessageType.Build(mailingId), mime.Headers["X-Postmaster-Msgtype"]);
    }
}

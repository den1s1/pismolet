using System.Reflection;
using Microsoft.Extensions.Logging;
using MimeKit;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class DirectLinkSmtpEmailProviderAdapterTests
{
    [Fact]
    public void PrepareMessage_restores_old_tracking_url_and_does_not_create_new_tracking_link()
    {
        var clickTracking = new InMemoryClickTrackingRepository();
        var oldLink = clickTracking.AddOrGet(TrackedLink.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "old-reader@example.test",
            "https://example.org/news?x=1&y=2"));
        var adapter = CreateAdapter(clickTracking);
        var newMailingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var userHtml = $"<div><a href=\"https://app.pismolet.ru/t/click/{oldLink.Token}\">https://example.org/news</a></div>";
        var body = MailingServiceEmailFooter.PlainText(
            userHtml,
            "Вы зарегистрировались на встречу.",
            "/unsubscribe/test-token",
            "Служебный идентификатор рассылки: TEST-1");
        var message = TestEmailMessage(newMailingId, body, MessageBodyFormat.Html);

        var prepared = InvokePrepareMessage(adapter, message);
        var mime = InvokeMimeMessage(adapter, prepared);
        var html = Assert.Single(TextParts(mime.Body), part => part.IsHtml);

        Assert.DoesNotContain("/t/click/", prepared.PlainTextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"https://example.org/news?x=1&amp;y=2\"", prepared.PlainTextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("/t/click/", html.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"https://example.org/news?x=1&amp;y=2\"", html.Text, StringComparison.Ordinal);
        Assert.Empty(clickTracking.ListLinksByMailingId(newMailingId));
    }

    [Fact]
    public void RestoreText_replaces_known_service_tracking_url_but_keeps_external_similar_path()
    {
        var token = new string('a', 64);
        var source = $"Старая ссылка: https://app.pismolet.ru/t/click/{token}\nВнешняя: https://example.org/t/click/{token}";

        var restored = LegacyClickTrackingLinkRestorer.RestoreText(
            source,
            value => value == token ? "https://example.org/original" : null);

        Assert.Contains("Старая ссылка: https://example.org/original", restored, StringComparison.Ordinal);
        Assert.Contains($"Внешняя: https://example.org/t/click/{token}", restored, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreHtml_keeps_unknown_tracking_token_unchanged()
    {
        var token = new string('b', 64);
        var source = $"<a href='https://app.pismolet.ru/t/click/{token}'>Ссылка</a>";

        var restored = LegacyClickTrackingLinkRestorer.RestoreHtml(source, _ => null);

        Assert.Equal(source, restored);
    }

    private static DirectLinkSmtpEmailProviderAdapter CreateAdapter(InMemoryClickTrackingRepository clickTracking)
    {
        var options = new SmtpEmailProviderOptions(
            Host: "127.0.0.1",
            Port: 25,
            Username: string.Empty,
            Password: string.Empty,
            FromEmail: "sender@pismolet.ru",
            FromName: "Письмолёт",
            SecureSocketOptions: "None",
            TimeoutSeconds: 1);

        return new DirectLinkSmtpEmailProviderAdapter(
            options,
            new PublicUrlOptions("https://app.pismolet.ru"),
            new SilentLogger<SmtpEmailProviderAdapter>(),
            clickTracking);
    }

    private static EmailMessage TestEmailMessage(Guid mailingId, string body, MessageBodyFormat bodyFormat) => new(
        mailingId,
        new EmailRecipient("reader@example.test"),
        "Sender",
        "Subject",
        body,
        "/unsubscribe/test-token",
        "Служебный идентификатор рассылки: TEST",
        "reply@test.pismolet.ru",
        "reply-token",
        new Dictionary<string, string>
        {
            ["mailingId"] = mailingId.ToString("N"),
            ["recipientKey"] = "recipient-key"
        },
        BodyFormat: bodyFormat);

    private static EmailMessage InvokePrepareMessage(DirectLinkSmtpEmailProviderAdapter adapter, EmailMessage message)
    {
        var method = typeof(DirectLinkSmtpEmailProviderAdapter).GetMethod("PrepareMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (EmailMessage)method.Invoke(adapter, new object[] { message })!;
    }

    private static MimeMessage InvokeMimeMessage(DirectLinkSmtpEmailProviderAdapter adapter, EmailMessage message)
    {
        var field = typeof(DirectLinkSmtpEmailProviderAdapter).GetField("inner", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var inner = (SmtpEmailProviderAdapter)field.GetValue(adapter)!;
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildMimeMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (MimeMessage)method.Invoke(inner, new object[] { message })!;
    }

    private static IReadOnlyCollection<TextPart> TextParts(MimeEntity? entity)
    {
        if (entity is null)
        {
            throw new InvalidOperationException("MIME body was not built.");
        }

        var result = new List<TextPart>();
        Collect(entity, result);
        return result;

        static void Collect(MimeEntity current, ICollection<TextPart> result)
        {
            if (current is TextPart text)
            {
                result.Add(text);
                return;
            }

            if (current is Multipart multipart)
            {
                foreach (var child in multipart)
                {
                    Collect(child, result);
                }
            }
        }
    }

    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}

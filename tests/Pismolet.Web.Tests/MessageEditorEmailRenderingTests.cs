using System.Reflection;
using Microsoft.Extensions.Logging;
using MimeKit;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MessageEditorEmailRenderingTests
{
    private static readonly Guid MailingId = Guid.Parse("4f17f7d6-7b77-4fa2-8725-c4a10eb63f93");

    [Fact]
    public void Html_message_tracks_auto_link_once_and_keeps_original_url_in_text_part()
    {
        var clickTracking = new InMemoryClickTrackingRepository();
        var adapter = CreateAdapter(clickTracking);
        var userHtml = HtmlMessageLinkifier.Linkify("<p>Новости: https://example.org/news.</p>");
        var body = MailingServiceEmailFooter.PlainText(
            userHtml,
            "Вы зарегистрировались на встречу.",
            "/unsubscribe/test-token",
            "Служебный идентификатор рассылки: TEST-1");
        var message = TestEmailMessage(body);

        var firstMime = InvokeMimeMessage(adapter, message);
        var secondMime = InvokeMimeMessage(adapter, message);

        var links = clickTracking.ListLinksByMailingId(MailingId);
        var trackedLink = Assert.Single(links);
        Assert.Equal("https://example.org/news", trackedLink.OriginalUrl);

        AssertMimeParts(firstMime, trackedLink.Token);
        AssertMimeParts(secondMime, trackedLink.Token);
    }

    [Fact]
    public void Html_message_does_not_track_unsubscribe_or_already_tracked_links_again()
    {
        var clickTracking = new InMemoryClickTrackingRepository();
        var adapter = CreateAdapter(clickTracking);
        const string userHtml = "<p><a href=\"https://app.pismolet.ru/t/click/existing-token\">Уже отслеживается</a></p>";
        var body = MailingServiceEmailFooter.PlainText(
            userHtml,
            "Вы зарегистрировались на встречу.",
            "/unsubscribe/test-token",
            "Служебный идентификатор рассылки: TEST-2");

        var mime = InvokeMimeMessage(adapter, TestEmailMessage(body));
        var html = Assert.Single(TextParts(mime.Body), part => part.IsHtml);

        Assert.Empty(clickTracking.ListLinksByMailingId(MailingId));
        Assert.Contains("href=\"https://app.pismolet.ru/t/click/existing-token\"", html.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/t/click/existing-token\">Отпис", html.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertMimeParts(MimeMessage mime, string token)
    {
        var parts = TextParts(mime.Body);
        var plain = Assert.Single(parts, part => part.IsPlain);
        var html = Assert.Single(parts, part => part.IsHtml);

        Assert.Contains("https://example.org/news", plain.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/t/click/", plain.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"href=\"https://app.pismolet.ru/t/click/{token}\"", html.Text, StringComparison.Ordinal);
        Assert.Contains(">https://example.org/news</a>.", html.Text, StringComparison.Ordinal);
    }

    private static SmtpEmailProviderAdapter CreateAdapter(InMemoryClickTrackingRepository clickTracking)
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

        return new SmtpEmailProviderAdapter(
            options,
            new PublicUrlOptions("https://app.pismolet.ru"),
            new SilentLogger<SmtpEmailProviderAdapter>(),
            clickTracking);
    }

    private static EmailMessage TestEmailMessage(string body) => new(
        MailingId,
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
            ["mailingId"] = MailingId.ToString("N"),
            ["recipientKey"] = "recipient-key"
        },
        BodyFormat: MessageBodyFormat.Html);

    private static MimeMessage InvokeMimeMessage(SmtpEmailProviderAdapter adapter, EmailMessage message)
    {
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildMimeMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (MimeMessage)method.Invoke(adapter, new object[] { message })!;
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

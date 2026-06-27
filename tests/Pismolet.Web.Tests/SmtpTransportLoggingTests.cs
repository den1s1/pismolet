using System.Reflection;
using Microsoft.Extensions.Logging;
using MimeKit;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class SmtpTransportLoggingTests
{
    [Theory]
    [InlineData("127.0.0.1", "LocalSmtp")]
    [InlineData("localhost", "LocalSmtp")]
    [InlineData("::1", "LocalSmtp")]
    [InlineData("smtp.timeweb.ru", "TimewebSmtp")]
    [InlineData("mail.pismolet.ru", "ExternalSmtp")]
    public void Smtp_transport_logging_classifies_transport(string host, string expectedTransport)
    {
        var adapter = CreateAdapter(host);

        var actual = InvokeTransportName(adapter);

        Assert.Equal(expectedTransport, actual);
    }

    [Theory]
    [InlineData("secret.user@example.test", "example.test")]
    [InlineData("Secret.User+Tag@Example.Test", "example.test")]
    [InlineData("sender@pismolet.ru", "pismolet.ru")]
    [InlineData("missing-at", "unknown")]
    [InlineData("", "unknown")]
    public void Smtp_transport_logging_uses_domain_only_for_email_log_fields(string email, string expectedDomain)
    {
        var actual = InvokeEmailDomain(email);

        Assert.Equal(expectedDomain, actual);
        Assert.DoesNotContain("@", actual);
        Assert.False(actual.Contains("secret.user", StringComparison.OrdinalIgnoreCase));
        Assert.False(actual.Contains("sender@", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Smtp_send_logging_records_transport_and_domains_without_full_email_addresses()
    {
        var logger = new CaptureLogger<SmtpEmailProviderAdapter>();
        var adapter = CreateAdapter("127.0.0.1", logger, port: 1);
        var message = new EmailMessage(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            new EmailRecipient("secret.user@gmail.com"),
            "Sender",
            "Subject",
            "Body",
            "/unsubscribe/test-token",
            "Service id",
            "reply@test.pismolet.ru",
            "reply-token",
            new Dictionary<string, string>
            {
                ["mailingId"] = "11111111222233334444555555555555",
                ["recipientKey"] = "recipient-key"
            });

        var result = await adapter.SendAsync(message, CancellationToken.None);

        Assert.False(result.Accepted);
        var started = Assert.Single(logger.Entries, x => x.LogLevel == LogLevel.Information && x.Message.Contains("SMTP send started", StringComparison.Ordinal));
        var failed = Assert.Single(logger.Entries, x => x.LogLevel == LogLevel.Warning && x.Message.Contains("SMTP send failed", StringComparison.Ordinal));
        Assert.Equal("LocalSmtp", started.Value("Transport"));
        Assert.Equal("127.0.0.1", started.Value("Host"));
        Assert.Equal(1, started.Value("Port"));
        Assert.Equal("pismolet.ru", started.Value("FromDomain"));
        Assert.Equal("gmail.com", started.Value("RecipientDomain"));
        Assert.Equal("LocalSmtp", failed.Value("Transport"));
        Assert.Equal("gmail.com", failed.Value("RecipientDomain"));
        Assert.DoesNotContain("secret.user@gmail.com", logger.RenderedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sender@pismolet.ru", logger.RenderedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Smtp_html_body_includes_open_tracking_pixel_when_url_is_present()
    {
        var html = InvokeHtmlBody(
            "Здравствуйте\nОтписаться: https://app.pismolet.ru/unsubscribe/test",
            "https://app.pismolet.ru/unsubscribe/test",
            "https://app.pismolet.ru/t/open/tracking-token.gif");

        Assert.Contains("<img src=\"https://app.pismolet.ru/t/open/tracking-token.gif\"", html, StringComparison.Ordinal);
        Assert.Contains("width=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("height=\"1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("file://", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Smtp_html_body_omits_open_tracking_pixel_when_url_is_missing()
    {
        var html = InvokeHtmlBody(
            "Здравствуйте",
            "https://app.pismolet.ru/unsubscribe/test",
            null);

        Assert.DoesNotContain("/t/open/", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Smtp_mime_message_treats_html_like_text_as_text_when_body_format_is_text()
    {
        var adapter = CreateAdapter("127.0.0.1");
        var message = TestEmailMessage(
            "Покажите строку <p data-marker=\"plain\"> как обычный текст.\nОтписаться: /unsubscribe/test",
            MessageBodyFormat.Text);

        var mime = InvokeMimeMessage(adapter, message);
        var parts = TextParts(mime.Body);
        var plain = Assert.Single(parts, x => x.IsPlain);
        var html = Assert.Single(parts, x => x.IsHtml);

        Assert.Contains("<p data-marker=\"plain\">", plain.Text, StringComparison.Ordinal);
        Assert.Contains("&lt;p data-marker=&quot;plain&quot;&gt;", html.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_mime_message_uses_html_body_when_body_format_is_html()
    {
        var adapter = CreateAdapter("127.0.0.1");
        var message = TestEmailMessage(
            "<h1>Hello</h1><p>HTML body</p>\nОтписаться: /unsubscribe/test",
            MessageBodyFormat.Html);

        var mime = InvokeMimeMessage(adapter, message);
        var parts = TextParts(mime.Body);
        var plain = Assert.Single(parts, x => x.IsPlain);
        var html = Assert.Single(parts, x => x.IsHtml);

        Assert.DoesNotContain("<h1>", plain.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", plain.Text, StringComparison.Ordinal);
        Assert.Contains("<h1>Hello</h1>", html.Text, StringComparison.Ordinal);
    }

    private static SmtpEmailProviderAdapter CreateAdapter(string host) => CreateAdapter(host, new SilentLogger<SmtpEmailProviderAdapter>());

    private static SmtpEmailProviderAdapter CreateAdapter(string host, ILogger<SmtpEmailProviderAdapter> logger, int port = 25)
    {
        var options = new SmtpEmailProviderOptions(
            Host: host,
            Port: port,
            Username: string.Empty,
            Password: string.Empty,
            FromEmail: "sender@pismolet.ru",
            FromName: "Письмолет",
            SecureSocketOptions: "None",
            TimeoutSeconds: 1);

        var publicUrlOptions = new PublicUrlOptions("https://app.pismolet.ru");

        return (SmtpEmailProviderAdapter)Activator.CreateInstance(
            typeof(SmtpEmailProviderAdapter),
            options,
            publicUrlOptions,
            logger,
            null)!;
    }

    private static string InvokeTransportName(SmtpEmailProviderAdapter adapter)
    {
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("GetTransportName", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)method.Invoke(adapter, Array.Empty<object>())!;
    }

    private static string InvokeEmailDomain(string? email)
    {
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("GetEmailDomain", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, new object?[] { email })!;
    }

    private static MimeMessage InvokeMimeMessage(SmtpEmailProviderAdapter adapter, EmailMessage message)
    {
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildMimeMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (MimeMessage)method.Invoke(adapter, new object[] { message })!;
    }

    private static EmailMessage TestEmailMessage(string body, MessageBodyFormat bodyFormat) => new(
        Guid.Parse("22222222-3333-4444-5555-666666666666"),
        new EmailRecipient("reader@example.test"),
        "Sender",
        "Subject",
        body,
        "/unsubscribe/test",
        "Service id",
        "reply@test.pismolet.ru",
        "reply-token",
        new Dictionary<string, string>
        {
            ["mailingId"] = "22222222333344445555666666666666",
            ["recipientKey"] = "recipient-key"
        },
        BodyFormat: bodyFormat);

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

    private static string InvokeHtmlBody(string plainText, string unsubscribeUrl, string? trackingPixelUrl)
    {
        var method = typeof(SmtpEmailProviderAdapter).GetMethod("BuildHtmlBody", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, new object?[] { plainText, unsubscribeUrl, trackingPixelUrl, null })!;
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, IReadOnlyList<KeyValuePair<string, object?>> State)
    {
        public string RenderedMessage => Message;

        public object? Value(string key) => State.FirstOrDefault(x => x.Key == key).Value;
    }

    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public string RenderedText => string.Join("\n", _entries.Select(x => x.RenderedMessage + " " + string.Join(" ", x.State.Select(item => item.Value))));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? Array.Empty<KeyValuePair<string, object?>>();
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), values.ToArray()));
        }
    }
}

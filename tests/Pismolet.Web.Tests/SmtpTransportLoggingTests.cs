using System.Reflection;
using Microsoft.Extensions.Logging;
using Pismolet.Web.Application.Mailings;
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

        var publicUrlOptionsType = typeof(SmtpEmailProviderAdapter)
            .GetConstructors()
            .Single()
            .GetParameters()[1]
            .ParameterType;
        var publicUrlOptions = Activator.CreateInstance(publicUrlOptionsType, "https://app.pismolet.ru")!;

        return (SmtpEmailProviderAdapter)Activator.CreateInstance(
            typeof(SmtpEmailProviderAdapter),
            options,
            publicUrlOptions,
            logger)!;
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

    private sealed record LogEntry(LogLevel LogLevel, string RenderedMessage, IReadOnlyList<KeyValuePair<string, object?>> State)
    {
        public string Message => RenderedMessage;

        public object? Value(string key) => State.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal)).Value;
    }
}

using System.Reflection;
using Microsoft.Extensions.Logging;
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

    private static SmtpEmailProviderAdapter CreateAdapter(string host)
    {
        var options = new SmtpEmailProviderOptions(
            Host: host,
            Port: 25,
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
            new SilentLogger<SmtpEmailProviderAdapter>())!;
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
}

using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed record SmtpEmailProviderOptions(
    string Host,
    int Port,
    bool EnableSsl,
    string UserName,
    string Password,
    string FromEmail,
    string FromName,
    TimeSpan Timeout)
{
    public static SmtpEmailProviderOptions FromConfiguration(IConfiguration configuration)
    {
        var host = Read(configuration, "Smtp:Host", "PISMOLET_SMTP_HOST");
        var userName = Read(configuration, "Smtp:UserName", "PISMOLET_SMTP_USERNAME");
        var password = Read(configuration, "Smtp:Password", "PISMOLET_SMTP_PASSWORD");
        var fromEmail = Read(configuration, "Smtp:FromEmail", "PISMOLET_SMTP_FROM_EMAIL");
        var fromName = ReadOptional(configuration, "Smtp:FromName", "PISMOLET_SMTP_FROM_NAME") ?? "Письмолет";
        var portRaw = ReadOptional(configuration, "Smtp:Port", "PISMOLET_SMTP_PORT");
        var sslRaw = ReadOptional(configuration, "Smtp:EnableSsl", "PISMOLET_SMTP_ENABLE_SSL");
        var timeoutRaw = ReadOptional(configuration, "Smtp:TimeoutSeconds", "PISMOLET_SMTP_TIMEOUT_SECONDS");

        var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 587;
        var enableSsl = !bool.TryParse(sslRaw, out var parsedSsl) || parsedSsl;
        var timeoutSeconds = int.TryParse(timeoutRaw, out var parsedTimeout) ? Math.Clamp(parsedTimeout, 5, 120) : 20;
        return new SmtpEmailProviderOptions(host, port, enableSsl, userName, password, fromEmail, fromName, TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static string Read(IConfiguration configuration, string key, string environmentKey)
    {
        var value = ReadOptional(configuration, key, environmentKey);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Для SMTP задайте {key} или {environmentKey}.")
            : value;
    }

    private static string? ReadOptional(IConfiguration configuration, string key, string environmentKey) =>
        configuration[key] ?? Environment.GetEnvironmentVariable(environmentKey);
}

public sealed class SmtpEmailProviderAdapter(SmtpEmailProviderOptions options) : IEmailProviderAdapter
{
    public async Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(options.FromEmail, string.IsNullOrWhiteSpace(message.SenderName) ? options.FromName : message.SenderName, Encoding.UTF8),
                Subject = message.Subject,
                SubjectEncoding = Encoding.UTF8,
                Body = message.PlainTextBody,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false
            };
            mail.To.Add(new MailAddress(message.Recipient.Email));
            AddUnsubscribeHeaders(mail, message.UnsubscribeUrl);

            using var client = new SmtpClient(options.Host, options.Port)
            {
                EnableSsl = options.EnableSsl,
                Credentials = new NetworkCredential(options.UserName, options.Password),
                Timeout = (int)options.Timeout.TotalMilliseconds
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            await client.SendMailAsync(mail, timeout.Token);
            return EmailProviderSendResult.Success($"smtp-{message.MailingId:N}-{Guid.NewGuid():N}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EmailProviderSendResult.Failure("smtp_timeout", $"SMTP provider не ответил за {options.Timeout.TotalSeconds:0} секунд.");
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            return EmailProviderSendResult.Failure("smtp_failed", ex.Message);
        }
    }

    private static void AddUnsubscribeHeaders(MailMessage mail, string unsubscribeUrl)
    {
        if (!Uri.TryCreate(unsubscribeUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        mail.Headers.Add("List-Unsubscribe", $"<{uri.AbsoluteUri}>");
        mail.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
    }
}

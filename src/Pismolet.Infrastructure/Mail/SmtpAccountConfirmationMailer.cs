using System.Collections.Concurrent;
using System.Net;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Domain.Mail;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class SmtpAccountConfirmationMailer : IFakeMailer
{
    private readonly ConcurrentQueue<FakeMail> _outbox = new();
    private readonly SmtpEmailProviderOptions _options;
    private readonly PublicUrlOptions _publicUrlOptions;
    private readonly ILogger<SmtpAccountConfirmationMailer> _logger;

    public SmtpAccountConfirmationMailer(
        IConfiguration configuration,
        PublicUrlOptions publicUrlOptions,
        ILogger<SmtpAccountConfirmationMailer> logger)
    {
        _options = ReadOptions(configuration);
        _publicUrlOptions = publicUrlOptions;
        _logger = logger;
    }

    public void SendConfirmation(string to, string subject, string link)
    {
        var absoluteLink = ToAbsoluteUrl(link);
        _outbox.Enqueue(new FakeMail(to, subject, absoluteLink, DateTimeOffset.UtcNow));

        try
        {
            var message = BuildMessage(to, subject, absoluteLink);
            Send(message);
            _logger.LogInformation(
                "Account confirmation email sent. host={Host} port={Port} recipientDomain={RecipientDomain}",
                _options.Host,
                _options.Port,
                GetEmailDomain(to));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Account confirmation email failed. host={Host} port={Port} recipientDomain={RecipientDomain}",
                _options.Host,
                _options.Port,
                GetEmailDomain(to));
        }
    }

    public void SendAdminNotification(string to, string subject, string body, string? link = null)
    {
        var absoluteLink = string.IsNullOrWhiteSpace(link) ? string.Empty : ToAbsoluteUrl(link);
        _outbox.Enqueue(new FakeMail(to, subject, absoluteLink, DateTimeOffset.UtcNow, TextBody: body));

        try
        {
            var message = BuildAdminNotificationMessage(to, subject, body, absoluteLink);
            Send(message);
            _logger.LogInformation(
                "Admin notification email sent. host={Host} port={Port} recipientDomain={RecipientDomain}",
                _options.Host,
                _options.Port,
                GetEmailDomain(to));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Admin notification email failed. host={Host} port={Port} recipientDomain={RecipientDomain}",
                _options.Host,
                _options.Port,
                GetEmailDomain(to));
        }
    }

    public void AddMailingMessage(
        string to,
        string subject,
        string link,
        string? replyToAddress = null,
        string? replyToken = null,
        string? providerMessageId = null,
        string? textBody = null) => _outbox.Enqueue(new FakeMail(
            To: to,
            Subject: subject,
            Link: link,
            CreatedAt: DateTimeOffset.UtcNow,
            ReplyToAddress: replyToAddress,
            ReplyToken: replyToken,
            ProviderMessageId: providerMessageId,
            TextBody: textBody));

    public void AddForwardedReply(string to, string subject, string fromEmail, string textBody, string providerMessageId) => _outbox.Enqueue(new FakeMail(
        To: to,
        Subject: subject,
        Link: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow,
        ProviderMessageId: providerMessageId,
        TextBody: textBody,
        FromEmail: fromEmail,
        IsForwardedReply: true));

    public IReadOnlyCollection<FakeMail> GetOutbox() => _outbox.Reverse().ToArray();

    private MimeMessage BuildMessage(string to, string subject, string link)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.MessageId = MimeUtils.GenerateMessageId(GetMessageIdDomain());

        var encodedLink = WebUtility.HtmlEncode(link);
        var body = new BodyBuilder
        {
            TextBody = string.Join("\n\n",
                "Здравствуйте!",
                "Для завершения регистрации в Письмолёте подтвердите email по ссылке:",
                link,
                "Если вы не регистрировались в Письмолёте, просто проигнорируйте это письмо."),
            HtmlBody = $"""
                <p>Здравствуйте!</p>
                <p>Для завершения регистрации в Письмолёте подтвердите email:</p>
                <p><a href="{encodedLink}">Подтвердить email</a></p>
                <p>Если вы не регистрировались в Письмолёте, просто проигнорируйте это письмо.</p>
                """
        };
        message.Body = body.ToMessageBody();
        return message;
    }

    private MimeMessage BuildAdminNotificationMessage(string to, string subject, string textBody, string link)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.MessageId = MimeUtils.GenerateMessageId(GetMessageIdDomain());

        var safeBody = WebUtility.HtmlEncode(textBody).Replace("\n", "<br>", StringComparison.Ordinal);
        var safeLink = string.IsNullOrWhiteSpace(link) ? string.Empty : WebUtility.HtmlEncode(link);
        var linkText = string.IsNullOrWhiteSpace(link) ? string.Empty : $"\n\nОткрыть: {link}";
        var linkHtml = string.IsNullOrWhiteSpace(link) ? string.Empty : $"""<p><a href="{safeLink}">Открыть в админке</a></p>""";
        message.Body = new BodyBuilder
        {
            TextBody = textBody + linkText,
            HtmlBody = $"""
                <p>{safeBody}</p>
                {linkHtml}
                """
        }.ToMessageBody();
        return message;
    }

    private void Send(MimeMessage message)
    {
        using var client = new SmtpClient { Timeout = Math.Max(1, _options.TimeoutSeconds) * 1000 };
        client.Connect(_options.Host, _options.Port, SmtpEmailProviderAdapterSafeOptions.Parse(_options.SecureSocketOptions, _options.Port));
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Authenticate(_options.Username, _options.Password);
        }

        client.Send(message);
        client.Disconnect(true);
    }

    private string ToAbsoluteUrl(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        var baseUrl = _publicUrlOptions.PublicBaseUrl.TrimEnd('/');
        var path = link.StartsWith('/') ? link : $"/{link}";
        return $"{baseUrl}{path}";
    }

    private string GetMessageIdDomain()
    {
        var at = _options.FromEmail.LastIndexOf('@');
        return at >= 0 && at < _options.FromEmail.Length - 1
            ? _options.FromEmail[(at + 1)..].Trim()
            : "pismolet.local";
    }

    private static SmtpEmailProviderOptions ReadOptions(IConfiguration configuration)
    {
        var host = Required(configuration, "Smtp:Host", "Smtp__Host");
        var port = ReadInt(configuration, "Smtp:Port", 587, 1, 65535);
        var username = configuration["Smtp:Username"] ?? string.Empty;
        var password = configuration["Smtp:Password"] ?? string.Empty;
        var fromEmail = configuration["Smtp:FromEmail"] ?? username;
        var fromName = configuration["Smtp:FromName"] ?? "Письмолёт";
        var secureSocketOptions = configuration["Smtp:SecureSocketOptions"] ?? configuration["Smtp:Security"] ?? (port == 465 ? "SslOnConnect" : "StartTlsWhenAvailable");
        var timeoutSeconds = ReadInt(configuration, "Smtp:TimeoutSeconds", 30, 1, 300);
        if (string.IsNullOrWhiteSpace(fromEmail)) throw new InvalidOperationException("Для SMTP задайте Smtp:FromEmail или Smtp:Username.");
        return new SmtpEmailProviderOptions(host.Trim(), port, username.Trim(), password, fromEmail.Trim(), string.IsNullOrWhiteSpace(fromName) ? "Письмолёт" : fromName.Trim(), secureSocketOptions.Trim(), timeoutSeconds);
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        var value = configuration[key] ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)];
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private static string Required(IConfiguration configuration, string key, string envKey)
    {
        var value = configuration[key] ?? configuration[envKey] ?? Environment.GetEnvironmentVariable(envKey);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Не задан обязательный параметр {key}.");
        return value;
    }

    private static string GetEmailDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..].Trim().ToLowerInvariant() : "unknown";
    }
}

internal static class SmtpEmailProviderAdapterSafeOptions
{
    public static MailKit.Security.SecureSocketOptions Parse(string value, int port)
    {
        if (Enum.TryParse<MailKit.Security.SecureSocketOptions>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return port == 465
            ? MailKit.Security.SecureSocketOptions.SslOnConnect
            : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;
    }
}

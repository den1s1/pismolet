using System.Collections.Concurrent;
using System.Net;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Domain.Mail;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class SmtpAccountConfirmationMailer(
    SmtpEmailProviderOptions options,
    PublicUrlOptions publicUrlOptions,
    ILogger<SmtpAccountConfirmationMailer> logger) : IFakeMailer
{
    private readonly ConcurrentQueue<FakeMail> _outbox = new();

    public void SendConfirmation(string to, string subject, string link)
    {
        var absoluteLink = ToAbsoluteUrl(link);
        _outbox.Enqueue(new FakeMail(to, subject, absoluteLink, DateTimeOffset.UtcNow));

        try
        {
            var message = BuildMessage(to, subject, absoluteLink);
            Send(message);
            logger.LogInformation(
                "Account confirmation email sent. host={Host} port={Port} recipientDomain={RecipientDomain}",
                options.Host,
                options.Port,
                GetEmailDomain(to));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Account confirmation email failed. host={Host} port={Port} recipientDomain={RecipientDomain}",
                options.Host,
                options.Port,
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
        message.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
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

    private void Send(MimeMessage message)
    {
        using var client = new SmtpClient { Timeout = Math.Max(1, options.TimeoutSeconds) * 1000 };
        client.Connect(options.Host, options.Port, SmtpEmailProviderAdapterSafeOptions.Parse(options.SecureSocketOptions, options.Port));
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            client.Authenticate(options.Username, options.Password);
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

        var baseUrl = publicUrlOptions.PublicBaseUrl.TrimEnd('/');
        var path = link.StartsWith('/') ? link : $"/{link}";
        return $"{baseUrl}{path}";
    }

    private string GetMessageIdDomain()
    {
        var at = options.FromEmail.LastIndexOf('@');
        return at >= 0 && at < options.FromEmail.Length - 1
            ? options.FromEmail[(at + 1)..].Trim()
            : "pismolet.local";
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

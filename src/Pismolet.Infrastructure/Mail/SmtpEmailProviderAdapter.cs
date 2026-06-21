using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed record SmtpEmailProviderOptions(
    string Host,
    int Port,
    string Username,
    string Password,
    string FromEmail,
    string FromName,
    string SecureSocketOptions,
    int TimeoutSeconds)
{
    public const string ProviderName = "Smtp";
}

public sealed class SmtpEmailProviderAdapter(
    SmtpEmailProviderOptions options,
    PublicUrlOptions publicUrlOptions,
    ILogger<SmtpEmailProviderAdapter> logger) : IEmailProviderAdapter
{
    public string ProviderName => GetTransportName();

    public async Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var transport = GetTransportName();
        var recipientDomain = GetEmailDomain(message.Recipient.Email);
        var fromDomain = GetEmailDomain(options.FromEmail);

        logger.LogInformation(
            "SMTP send started. transport={Transport} host={Host} port={Port} fromDomain={FromDomain} recipientDomain={RecipientDomain} subjectLength={SubjectLength}",
            transport,
            options.Host,
            options.Port,
            fromDomain,
            recipientDomain,
            message.Subject?.Length ?? 0);

        try
        {
            var normalized = NormalizeMessage(message);
            var mime = BuildMimeMessage(normalized);
            await SendMimeMessageAsync(mime, cancellationToken);
            var providerMessageId = mime.MessageId ?? $"smtp-{Guid.NewGuid():N}";

            logger.LogInformation(
                "SMTP send succeeded. transport={Transport} host={Host} port={Port} fromDomain={FromDomain} recipientDomain={RecipientDomain} providerMessageId={ProviderMessageId}",
                transport,
                options.Host,
                options.Port,
                fromDomain,
                recipientDomain,
                providerMessageId);

            return EmailProviderSendResult.Success(EnvelopeProviderPayload(providerMessageId));
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or IOException or SocketException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(
                ex,
                "SMTP send failed. transport={Transport} host={Host} port={Port} fromDomain={FromDomain} recipientDomain={RecipientDomain}",
                transport,
                options.Host,
                options.Port,
                fromDomain,
                recipientDomain);

            return EmailProviderSendResult.Failure(EnvelopeProviderPayload("smtp_send_failed"), ex.Message);
        }
    }

    public Task<EmailProviderWebhookParseResult> ParseWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
        Task.FromResult(EmailProviderWebhookParseResult.Failure("SMTP adapter does not support provider delivery webhooks."));

    public Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
        Task.FromResult(EmailProviderInboundParseResult.Failure("SMTP adapter does not support inbound webhooks."));

    public async Task<EmailProviderSendResult> ForwardReplyToClientAsync(ReplyEvent replyEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replyEvent.ForwardToEmailNormalized))
        {
            return EmailProviderSendResult.Failure("missing_forward_to", "Не указан адрес пересылки клиента.");
        }

        var transport = GetTransportName();
        var recipientDomain = GetEmailDomain(replyEvent.ForwardToEmailNormalized);
        var fromDomain = GetEmailDomain(options.FromEmail);

        logger.LogInformation(
            "SMTP reply forward started. transport={Transport} host={Host} port={Port} fromDomain={FromDomain} recipientDomain={RecipientDomain} replyEventId={ReplyEventId}",
            transport,
            options.Host,
            options.Port,
            fromDomain,
            recipientDomain,
            replyEvent.Id);

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
            mime.To.Add(MailboxAddress.Parse(replyEvent.ForwardToEmailNormalized));
            mime.Subject = $"Ответ на рассылку: {replyEvent.SubjectPreview}";
            mime.MessageId = MimeUtils.GenerateMessageId(GetMessageIdDomain());
            mime.Body = new TextPart("plain")
            {
                Text = string.Join("\n\n",
                    "Это пересланный ответ получателя через сервис Письмолёт.",
                    $"От: {replyEvent.FromEmailNormalized}",
                    $"Получен: {replyEvent.ReceivedAt:yyyy-MM-dd HH:mm} UTC",
                    "Текст ответа:",
                    string.IsNullOrWhiteSpace(replyEvent.BodyTextStored) ? "[Тело ответа уже удалено или не сохранялось]" : replyEvent.BodyTextStored)
            };

            await SendMimeMessageAsync(mime, cancellationToken);
            var providerMessageId = mime.MessageId ?? $"smtp-forward-{replyEvent.Id:N}";

            logger.LogInformation(
                "SMTP reply forward succeeded. transport={Transport} host={Host} port={Port} fromDomain={FromDomain} recipientDomain={RecipientDomain} replyEventId={ReplyEventId} providerMessageId={ProviderMessageId}",
                transport,
                options.Host,
                options.Port,
                fromDomain,
                recipientDomain,
                replyEvent.Id,
                providerMessageId);

            return EmailProviderSendResult.Success(EnvelopeProviderPayload(providerMessageId));
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or IOException or SocketException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(
                ex,
                "SMTP reply forward failed. transport={Transport} host={Host} port={Port} fromDomain={FromDomain} recipientDomain={RecipientDomain} replyEventId={ReplyEventId}",
                transport,
                options.Host,
                options.Port,
                fromDomain,
                recipientDomain,
                replyEvent.Id);

            return EmailProviderSendResult.Failure(EnvelopeProviderPayload("smtp_forward_failed"), ex.Message);
        }
    }

    private EmailMessage NormalizeMessage(EmailMessage message)
    {
        var unsubscribeUrl = ToAbsoluteUrl(message.UnsubscribeUrl);
        var plainTextBody = ReplaceRelativeUrl(message.PlainTextBody, message.UnsubscribeUrl, unsubscribeUrl);
        plainTextBody = ReplaceVisibleRelativeUnsubscribeLinks(plainTextBody);
        plainTextBody = KeepSingleVisibleUnsubscribeLink(plainTextBody, unsubscribeUrl);
        var metadata = new Dictionary<string, string>(message.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["listUnsubscribe"] = $"<{unsubscribeUrl}>",
            ["listUnsubscribePost"] = "List-Unsubscribe=One-Click"
        };

        return message with
        {
            PlainTextBody = plainTextBody,
            UnsubscribeUrl = unsubscribeUrl,
            Metadata = metadata
        };
    }

    private MimeMessage BuildMimeMessage(EmailMessage message)
    {
        var mime = new MimeMessage();
        var displayName = string.IsNullOrWhiteSpace(message.SenderName) ? options.FromName : message.SenderName.Trim();
        var unsubscribeUrl = ToAbsoluteUrl(message.UnsubscribeUrl);
        var textBody = ReplaceRelativeUrl(message.PlainTextBody, message.UnsubscribeUrl, unsubscribeUrl);
        textBody = ReplaceVisibleRelativeUnsubscribeLinks(textBody);
        textBody = KeepSingleVisibleUnsubscribeLink(textBody, unsubscribeUrl);

        mime.From.Add(new MailboxAddress(displayName, options.FromEmail));
        mime.To.Add(MailboxAddress.Parse(message.Recipient.Email));
        mime.Subject = message.Subject;
        mime.MessageId = MimeUtils.GenerateMessageId(GetMessageIdDomain());

        if (!string.IsNullOrWhiteSpace(message.ReplyToAddress) && MailboxAddress.TryParse(message.ReplyToAddress, out var replyTo))
        {
            mime.ReplyTo.Add(replyTo);
        }

        if (!string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            mime.Headers.Replace("List-Unsubscribe", $"<{unsubscribeUrl}>");
            mime.Headers.Replace("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        if (message.Metadata.TryGetValue("mailingId", out var mailingId))
        {
            mime.Headers.Replace("X-Pismolet-Mailing-Id", mailingId);
        }

        if (message.Metadata.TryGetValue("recipientKey", out var recipientKey))
        {
            mime.Headers.Replace("X-Pismolet-Recipient-Key", recipientKey);
        }

        var body = new BodyBuilder
        {
            TextBody = textBody,
            HtmlBody = BuildHtmlBody(textBody, unsubscribeUrl)
        };
        mime.Body = body.ToMessageBody();
        return mime;
    }

    private async Task SendMimeMessageAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = Math.Max(1, options.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(options.Host, options.Port, ParseSecureSocketOptions(options.SecureSocketOptions, options.Port), cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static SecureSocketOptions ParseSecureSocketOptions(string value, int port)
    {
        if (Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return port == 465 ? MailKit.Security.SecureSocketOptions.SslOnConnect : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;
    }

    private string EnvelopeProviderPayload(string payload) => $"{ProviderName}{SendEvent.ProviderEnvelopeSeparator}{payload}";

    private string GetMessageIdDomain()
    {
        var at = options.FromEmail.LastIndexOf('@');
        if (at >= 0 && at < options.FromEmail.Length - 1)
        {
            return options.FromEmail[(at + 1)..].Trim();
        }

        return "pismolet.local";
    }

    private string GetTransportName()
    {
        var host = options.Host.Trim();
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            return "LocalSmtp";
        }

        return host.Contains("timeweb", StringComparison.OrdinalIgnoreCase)
            ? "TimewebSmtp"
            : "ExternalSmtp";
    }

    private static string GetEmailDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "unknown";
        }

        var at = email.LastIndexOf('@');
        if (at < 0 || at >= email.Length - 1)
        {
            return "unknown";
        }

        return email[(at + 1)..].Trim().ToLowerInvariant();
    }

    private string ToAbsoluteUrl(string relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
        {
            return relativeOrAbsolute;
        }

        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var baseUrl = publicUrlOptions.BaseUrl.TrimEnd('/');
        var path = relativeOrAbsolute.StartsWith('/') ? relativeOrAbsolute : $"/{relativeOrAbsolute}";
        return $"{baseUrl}{path}";
    }

    private string ReplaceRelativeUrl(string text, string relative, string absolute)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(relative) || string.IsNullOrWhiteSpace(absolute))
        {
            return text;
        }

        return text.Replace(relative, absolute, StringComparison.Ordinal);
    }

    private string ReplaceVisibleRelativeUnsubscribeLinks(string text)
    {
        var absolute = ToAbsoluteUrl("/unsubscribe/");
        return Regex.Replace(text, @"(?<!https://app\.pismolet\.ru)/unsubscribe/", absolute, RegexOptions.IgnoreCase);
    }

    private static string KeepSingleVisibleUnsubscribeLink(string text, string unsubscribeUrl)
    {
        if (string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            return text;
        }

        var first = text.IndexOf(unsubscribeUrl, StringComparison.Ordinal);
        if (first < 0)
        {
            return text;
        }

        var second = text.IndexOf(unsubscribeUrl, first + unsubscribeUrl.Length, StringComparison.Ordinal);
        return second < 0 ? text : text.Remove(second, unsubscribeUrl.Length).Insert(second, "ссылку выше");
    }

    private static string BuildHtmlBody(string plainText, string unsubscribeUrl)
    {
        var html = WebUtility.HtmlEncode(plainText).Replace("\n", "<br>\n", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            var encodedUrl = WebUtility.HtmlEncode(unsubscribeUrl);
            html = html.Replace(encodedUrl, $"<a href=\"{encodedUrl}\">Отписаться</a>", StringComparison.Ordinal);
        }

        return $"<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222;\"><p>{html}</p></body></html>";
    }
}

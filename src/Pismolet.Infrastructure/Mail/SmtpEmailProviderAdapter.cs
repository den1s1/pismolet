using System.Net;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
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

public sealed class SmtpEmailProviderAdapter(SmtpEmailProviderOptions options, PublicUrlOptions publicUrlOptions) : IEmailProviderAdapter
{
    public string ProviderName => SmtpEmailProviderOptions.ProviderName;

    public async Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var normalized = NormalizeMessage(message);
            var mime = BuildMimeMessage(normalized);
            await SendMimeMessageAsync(mime, cancellationToken);
            return EmailProviderSendResult.Success(mime.MessageId ?? $"smtp-{Guid.NewGuid():N}");
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or IOException or InvalidOperationException or TimeoutException)
        {
            return EmailProviderSendResult.Failure("smtp_send_failed", ex.Message);
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
            return EmailProviderSendResult.Success(mime.MessageId ?? $"smtp-forward-{replyEvent.Id:N}");
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or IOException or InvalidOperationException or TimeoutException)
        {
            return EmailProviderSendResult.Failure("smtp_forward_failed", ex.Message);
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

    private string GetMessageIdDomain()
    {
        var at = options.FromEmail.LastIndexOf('@');
        if (at >= 0 && at < options.FromEmail.Length - 1)
        {
            return options.FromEmail[(at + 1)..].Trim();
        }

        return "pismolet.local";
    }

    private string ToAbsoluteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrWhiteSpace(absoluteUri.Host))
        {
            return url;
        }

        var baseUrl = publicUrlOptions.PublicBaseUrl.Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(url) ? "/" : url.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return baseUrl + path;
    }

    private static string ReplaceRelativeUrl(string body, string relativeUrl, string absoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(relativeUrl) || relativeUrl == absoluteUrl)
        {
            return body;
        }

        return body.Replace(relativeUrl, absoluteUrl, StringComparison.Ordinal);
    }

    private string ReplaceVisibleRelativeUnsubscribeLinks(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        var baseUrl = publicUrlOptions.PublicBaseUrl.Trim().TrimEnd('/');
        var result = Regex.Replace(
            body,
            @"https?://localhost(?::\d+)?/unsubscribe/",
            baseUrl + "/unsubscribe/",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Regex.Replace(
            result,
            @"(^|[\s:('""=])(/unsubscribe/)",
            match => match.Groups[1].Value + baseUrl + "/unsubscribe/",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    }

    private static string KeepSingleVisibleUnsubscribeLink(string body, string unsubscribeUrl)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            return body;
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var result = new List<string>(lines.Length);
        var kept = false;

        foreach (var line in lines)
        {
            if (line.Contains(unsubscribeUrl, StringComparison.OrdinalIgnoreCase))
            {
                if (kept)
                {
                    continue;
                }

                kept = true;
            }

            result.Add(line);
        }

        return string.Join(Environment.NewLine, result).TrimEnd();
    }

    private static string BuildHtmlBody(string plainTextBody, string unsubscribeUrl)
    {
        var lines = (plainTextBody ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var htmlLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (IsVisibleUnsubscribeLine(line, unsubscribeUrl))
            {
                htmlLines.Add($"<p><a href=\"{HtmlAttributeEncode(unsubscribeUrl)}\">Отписаться</a> от всех рассылок через сервис Письмолёт.</p>");
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                htmlLines.Add("<br>");
                continue;
            }

            htmlLines.Add($"<p>{WebUtility.HtmlEncode(line)}</p>");
        }

        return "<!doctype html>" +
               "<html><head><meta charset=\"utf-8\"></head>" +
               "<body style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222;\">" +
               string.Join(Environment.NewLine, htmlLines) +
               "</body></html>";
    }

    private static bool IsVisibleUnsubscribeLine(string line, string unsubscribeUrl)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(unsubscribeUrl) && line.Contains(unsubscribeUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return line.Contains("/unsubscribe/", StringComparison.OrdinalIgnoreCase) &&
               line.Contains("Отпис", StringComparison.OrdinalIgnoreCase);
    }

    private static string HtmlAttributeEncode(string value) => WebUtility.HtmlEncode(value) ?? string.Empty;
}

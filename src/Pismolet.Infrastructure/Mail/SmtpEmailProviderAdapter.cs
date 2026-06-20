using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
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
        mime.From.Add(new MailboxAddress(displayName, options.FromEmail));
        mime.To.Add(MailboxAddress.Parse(message.Recipient.Email));
        mime.Subject = message.Subject;
        mime.MessageId = MimeUtils.GenerateMessageId(GetMessageIdDomain());

        if (!string.IsNullOrWhiteSpace(message.ReplyToAddress) && MailboxAddress.TryParse(message.ReplyToAddress, out var replyTo))
        {
            mime.ReplyTo.Add(replyTo);
        }

        if (message.Metadata.TryGetValue("listUnsubscribe", out var listUnsubscribe) && !string.IsNullOrWhiteSpace(listUnsubscribe))
        {
            mime.Headers.Replace(HeaderId.ListUnsubscribe, listUnsubscribe);
        }

        if (message.Metadata.TryGetValue("listUnsubscribePost", out var listUnsubscribePost) && !string.IsNullOrWhiteSpace(listUnsubscribePost))
        {
            mime.Headers.Replace("List-Unsubscribe-Post", listUnsubscribePost);
        }

        if (message.Metadata.TryGetValue("mailingId", out var mailingId))
        {
            mime.Headers.Replace("X-Pismolet-Mailing-Id", mailingId);
        }

        if (message.Metadata.TryGetValue("recipientKey", out var recipientKey))
        {
            mime.Headers.Replace("X-Pismolet-Recipient-Key", recipientKey);
        }

        mime.Body = new TextPart("plain") { Text = message.PlainTextBody };
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
        if (MailboxAddress.TryParse(options.FromEmail, out var mailbox) && mailbox.Domain is { Length: > 0 })
        {
            return mailbox.Domain;
        }

        return "pismolet.local";
    }

    private string ToAbsoluteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
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
        if (string.IsNullOrWhiteSpace(relativeUrl) || relativeUrl == absoluteUrl)
        {
            return body;
        }

        return body.Replace(relativeUrl, absoluteUrl, StringComparison.Ordinal);
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
}

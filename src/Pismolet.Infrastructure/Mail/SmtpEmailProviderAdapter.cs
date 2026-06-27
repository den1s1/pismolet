using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
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
    ILogger<SmtpEmailProviderAdapter> logger,
    IClickTrackingRepository? clickTracking = null) : IEmailProviderAdapter
{
    private const string FallbackPublicBaseUrl = "https://app.pismolet.ru";

    public string ProviderName => GetTransportName();

    public async Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var transport = GetTransportName();
        var recipientDomain = GetEmailDomain(message.Recipient.Email);
        var fromDomain = GetEmailDomain(options.FromEmail);

        logger.LogInformation(
            "SMTP send started. transport={Transport} host={Host} port={Port} fromDomain={FromDomain} recipientDomain={RecipientDomain} subjectLength={SubjectLength} attachmentCount={AttachmentCount}",
            transport,
            options.Host,
            options.Port,
            fromDomain,
            recipientDomain,
            message.Subject?.Length ?? 0,
            message.Attachments?.Count ?? 0);

        try
        {
            var normalized = NormalizeMessage(message);
            var mime = BuildMimeMessage(normalized);
            var smtpResponse = await SendMimeMessageAsync(mime, cancellationToken);
            var providerMessageId = PostfixQueueIdExtractor.TryExtract(smtpResponse) ?? mime.MessageId ?? $"smtp-{Guid.NewGuid():N}";

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

            var smtpResponse = await SendMimeMessageAsync(mime, cancellationToken);
            var providerMessageId = PostfixQueueIdExtractor.TryExtract(smtpResponse) ?? mime.MessageId ?? $"smtp-forward-{replyEvent.Id:N}";

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
        var trackingPixelUrl = BuildTrackingPixelUrl(message);
        var clickTrackingUrlFactory = BuildClickTrackingUrlFactory(message);
        var isHtmlBody = LooksLikeHtmlBody(textBody);

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
            TextBody = isHtmlBody ? BuildPlainTextFallbackFromHtml(textBody) : textBody,
            HtmlBody = isHtmlBody
                ? BuildHtmlBodyFromHtml(textBody, unsubscribeUrl, trackingPixelUrl, clickTrackingUrlFactory)
                : BuildHtmlBody(textBody, unsubscribeUrl, trackingPixelUrl, clickTrackingUrlFactory)
        };
        AddAttachments(body, message.Attachments);
        mime.Body = body.ToMessageBody();
        return mime;
    }

    private static void AddAttachments(BodyBuilder body, IReadOnlyCollection<EmailAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return;
        }

        foreach (var attachment in attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ParseAttachmentContentType(attachment.ContentType));
        }
    }

    private static ContentType ParseAttachmentContentType(string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            try
            {
                return ContentType.Parse(contentType);
            }
            catch (FormatException)
            {
            }
        }

        return new ContentType("application", "octet-stream");
    }

    private async Task<string?> SendMimeMessageAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = Math.Max(1, options.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(options.Host, options.Port, ParseSecureSocketOptions(options.SecureSocketOptions, options.Port), cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
        }

        var smtpResponse = await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return smtpResponse;
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

        var value = relativeOrAbsolute.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            if (IsHttpOrHttps(absolute))
            {
                return absolute.ToString();
            }

            value = string.IsNullOrWhiteSpace(absolute.PathAndQuery) ? absolute.AbsolutePath : absolute.PathAndQuery;
            if (string.IsNullOrWhiteSpace(value) || value == "/")
            {
                return GetPublicBaseUrl();
            }
        }

        var baseUrl = GetPublicBaseUrl();
        var path = value.StartsWith('/') ? value : $"/{value}";
        return $"{baseUrl}{path}";
    }

    private string GetPublicBaseUrl()
    {
        var configured = publicUrlOptions.PublicBaseUrl?.Trim();
        if (Uri.TryCreate(configured?.TrimEnd('/'), UriKind.Absolute, out var configuredUri) && IsHttpOrHttps(configuredUri))
        {
            return configuredUri.ToString().TrimEnd('/');
        }

        logger.LogWarning(
            "Invalid public base URL for SMTP email links. configuredScheme={ConfiguredScheme} configuredLength={ConfiguredLength}. Falling back to {FallbackPublicBaseUrl}.",
            Uri.TryCreate(configured, UriKind.Absolute, out var invalidUri) ? invalidUri.Scheme : "invalid",
            configured?.Length ?? 0,
            FallbackPublicBaseUrl);

        return FallbackPublicBaseUrl;
    }

    private static bool IsHttpOrHttps(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

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
        return Regex.Replace(
            text,
            @"(^|[\s<>""'])/unsubscribe/",
            match => $"{match.Groups[1].Value}{absolute}",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
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

    private string BuildTrackingPixelUrl(EmailMessage message)
    {
        var token = SendEvent.BuildTrackingToken(message.MailingId, message.Recipient.Email);
        return ToAbsoluteUrl($"/t/open/{token}.gif");
    }

    private Func<string, string?>? BuildClickTrackingUrlFactory(EmailMessage message)
    {
        if (clickTracking is null)
        {
            return null;
        }

        return originalUrl =>
        {
            try
            {
                var trackedLink = clickTracking.AddOrGet(TrackedLink.Create(message.MailingId, message.Recipient.Email, originalUrl));
                return ToAbsoluteUrl($"/t/click/{trackedLink.Token}");
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Skipped click tracking link. mailingId={MailingId} recipientDomain={RecipientDomain}", message.MailingId, GetEmailDomain(message.Recipient.Email));
                return null;
            }
        };
    }

    private static bool LooksLikeHtmlBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("<!doctype", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<table", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<div", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<p", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<br", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<h1", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<a ", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHtmlBody(string plainText, string unsubscribeUrl, string? trackingPixelUrl, Func<string, string?>? clickTrackingUrlFactory = null)
    {
        var html = WebUtility.HtmlEncode(plainText).Replace("\n", "<br>\n", StringComparison.Ordinal);
        if (clickTrackingUrlFactory is not null)
        {
            html = EmailClickTrackingHtmlRewriter.RewriteHtmlEncodedPlainTextLinks(html, unsubscribeUrl, clickTrackingUrlFactory);
        }

        if (!string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            var encodedUrl = WebUtility.HtmlEncode(unsubscribeUrl);
            html = html.Replace(encodedUrl, $"<a href=\"{encodedUrl}\">Отписаться</a>", StringComparison.Ordinal);
        }

        var trackingPixel = BuildTrackingPixelHtml(trackingPixelUrl);
        return $"<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222;\"><p>{html}</p>{trackingPixel}</body></html>";
    }

    private static string BuildHtmlBodyFromHtml(string htmlText, string unsubscribeUrl, string? trackingPixelUrl, Func<string, string?>? clickTrackingUrlFactory = null)
    {
        var source = RemoveUnsupportedHtml(htmlText.Trim());
        var documentHtml = source;
        var footerText = string.Empty;
        var htmlEnd = LastIndexOfOrdinalIgnoreCase(source, "</html>");
        if (htmlEnd >= 0)
        {
            var end = htmlEnd + "</html>".Length;
            documentHtml = source[..end];
            footerText = source[end..].Trim();
        }

        if (clickTrackingUrlFactory is not null)
        {
            documentHtml = RewriteRawHtmlLinks(documentHtml, unsubscribeUrl, clickTrackingUrlFactory);
        }

        var additions = BuildHtmlFooterFromPlainText(footerText, unsubscribeUrl) + BuildTrackingPixelHtml(trackingPixelUrl);
        if (!string.IsNullOrWhiteSpace(additions))
        {
            documentHtml = InsertBeforeClosingBody(documentHtml, additions);
        }

        return EnsureHtmlDocument(documentHtml);
    }

    private static string BuildPlainTextFallbackFromHtml(string htmlText)
    {
        var text = Regex.Replace(htmlText, @"<\s*(script|style)[^>]*>.*?</\s*\1\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<\s*head[^>]*>.*?</\s*head\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|tr|table|h[1-6]|li|section|article)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ", RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t\f\v]+", " ");
        text = Regex.Replace(text, @" ?\n ?", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string BuildHtmlFooterFromPlainText(string footerText, string unsubscribeUrl)
    {
        if (string.IsNullOrWhiteSpace(footerText))
        {
            return string.Empty;
        }

        var paragraphs = footerText
            .Split(new[] { "\r\n\r\n", "\n\n", "\r\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph => WebUtility.HtmlEncode(paragraph.Trim()).Replace("\n", "<br>\n", StringComparison.Ordinal))
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .ToArray();
        if (paragraphs.Length == 0)
        {
            return string.Empty;
        }

        var encodedUnsubscribeUrl = WebUtility.HtmlEncode(unsubscribeUrl);
        var htmlParagraphs = string.Join(string.Empty, paragraphs.Select(paragraph => $"<p>{paragraph.Replace(encodedUnsubscribeUrl, $"<a href=\"{encodedUnsubscribeUrl}\">Отписаться</a>", StringComparison.Ordinal)}</p>"));
        return $"<div style=\"margin-top:24px;padding-top:14px;border-top:1px solid #dbe4ef;color:#64748b;font-size:12px;line-height:1.45;\">{htmlParagraphs}</div>";
    }

    private static string RewriteRawHtmlLinks(string html, string unsubscribeUrl, Func<string, string?> clickTrackingUrlFactory)
    {
        return Regex.Replace(
            html,
            "href\\s*=\\s*([\"'])(.*?)\\1",
            match =>
            {
                var quote = match.Groups[1].Value;
                var rawUrl = WebUtility.HtmlDecode(match.Groups[2].Value);
                if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.Equals(unsubscribeUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }

                var trackedUrl = clickTrackingUrlFactory(rawUrl);
                return string.IsNullOrWhiteSpace(trackedUrl)
                    ? match.Value
                    : $"href={quote}{WebUtility.HtmlEncode(trackedUrl)}{quote}";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static string RemoveUnsupportedHtml(string html)
    {
        html = Regex.Replace(html, @"<\s*(script|iframe|object|embed|form)[^>]*>.*?</\s*\1\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return Regex.Replace(html, @"<\s*/?\s*(script|iframe|object|embed|form)[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static string EnsureHtmlDocument(string html)
    {
        if (html.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        return $"<!doctype html><html><head><meta charset=\"utf-8\"></head><body>{html}</body></html>";
    }

    private static string InsertBeforeClosingBody(string html, string addition)
    {
        var index = LastIndexOfOrdinalIgnoreCase(html, "</body>");
        return index < 0
            ? html + addition
            : html.Insert(index, addition);
    }

    private static string BuildTrackingPixelHtml(string? trackingPixelUrl) => string.IsNullOrWhiteSpace(trackingPixelUrl)
        ? string.Empty
        : $"<img src=\"{WebUtility.HtmlEncode(trackingPixelUrl)}\" width=\"1\" height=\"1\" alt=\"\" style=\"display:none;width:1px;height:1px;opacity:0\" />";

    private static int LastIndexOfOrdinalIgnoreCase(string value, string search) => value.LastIndexOf(search, StringComparison.OrdinalIgnoreCase);
}

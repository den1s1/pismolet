using System.Security.Cryptography;
using System.Text;
using MimeKit;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class PostfixRawMimeInboundReplyParser : IInboundReplyMimeParser
{
    private static readonly char At = Convert.ToChar(64);

    public Task<EmailProviderInboundParseResult> ParseAsync(InboundReplyRawMessage rawMessage, CancellationToken cancellationToken)
    {
        if (rawMessage.RawMime.Length == 0)
        {
            return Task.FromResult(EmailProviderInboundParseResult.Failure("Пустое входящее письмо."));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(rawMessage.RawMime);
            var mime = MimeMessage.Load(stream, cancellationToken);
            var headers = mime.Headers
                .GroupBy(x => x.Field, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => string.Join("\n", x.Select(y => y.Value)), StringComparer.OrdinalIgnoreCase);
            var from = mime.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
            var to = ResolveRecipientAddress(rawMessage.EnvelopeRecipient, mime, headers);
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                return Task.FromResult(EmailProviderInboundParseResult.Failure("Не удалось определить отправителя или получателя входящего ответа."));
            }

            var eventItem = new EmailProviderInboundEvent(
                Provider: "PostfixSpool",
                ProviderInboundEventId: BuildProviderInboundEventId(mime, rawMessage.SourceId, rawMessage.RawMime),
                FromEmail: from.Trim().ToLowerInvariant(),
                ToAddress: to.Trim().ToLowerInvariant(),
                ReplyToken: ExtractReplyToken(to),
                Subject: string.IsNullOrWhiteSpace(mime.Subject) ? "Ответ без темы" : mime.Subject.Trim(),
                TextBody: string.IsNullOrWhiteSpace(mime.TextBody) ? null : mime.TextBody.Trim(),
                HtmlBody: string.IsNullOrWhiteSpace(mime.HtmlBody) ? null : mime.HtmlBody.Trim(),
                Headers: headers,
                ReceivedAt: mime.Date == default ? DateTimeOffset.UtcNow : mime.Date.ToUniversalTime(),
                RawPayload: Encoding.UTF8.GetString(rawMessage.RawMime));

            return Task.FromResult(EmailProviderInboundParseResult.Success(eventItem));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or IOException or InvalidOperationException)
        {
            return Task.FromResult(EmailProviderInboundParseResult.Failure("Не удалось разобрать MIME входящего ответа."));
        }
    }

    private static string ResolveRecipientAddress(string? envelopeRecipient, MimeMessage message, IReadOnlyDictionary<string, string> headers)
    {
        var envelope = CleanAddress(envelopeRecipient);
        if (!string.IsNullOrWhiteSpace(envelope))
        {
            return envelope;
        }

        var original = ReadHeaderAddress(headers, "X-Original-To");
        if (!string.IsNullOrWhiteSpace(original))
        {
            return original;
        }

        var delivered = ReadHeaderAddress(headers, "Delivered-To");
        if (!string.IsNullOrWhiteSpace(delivered))
        {
            return delivered;
        }

        return message.To.Mailboxes.FirstOrDefault()?.Address
            ?? message.Cc.Mailboxes.FirstOrDefault()?.Address
            ?? string.Empty;
    }

    private static string ReadHeaderAddress(IReadOnlyDictionary<string, string> headers, string headerName)
    {
        if (!headers.TryGetValue(headerName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return MailboxAddress.TryParse(value, out var parsed) ? parsed.Address : CleanAddress(value);
    }

    private static string CleanAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = value.Trim().Trim('<', '>', ' ', '\t', '\r', '\n');
        return clean.IndexOf(At) >= 0 ? clean : string.Empty;
    }

    private static string? ExtractReplyToken(string address)
    {
        var clean = CleanAddress(address);
        var at = clean.IndexOf(At);
        if (at <= 0)
        {
            return null;
        }

        var localPart = clean[..at];
        if (localPart.StartsWith("reply+", StringComparison.OrdinalIgnoreCase) && localPart.Length > "reply+".Length)
        {
            return localPart["reply+".Length..];
        }

        return localPart.IndexOf('+') >= 0 ? null : localPart;
    }

    private static string BuildProviderInboundEventId(MimeMessage message, string sourceId, byte[] raw)
    {
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            return "postfix-" + Hash(message.MessageId.Trim())[..24];
        }

        return !string.IsNullOrWhiteSpace(sourceId)
            ? "postfix-source-" + Hash(sourceId.Trim())[..24]
            : "postfix-raw-" + Hash(raw)[..24];
    }

    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value)
    {
        var hash = SHA256.HashData(value);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

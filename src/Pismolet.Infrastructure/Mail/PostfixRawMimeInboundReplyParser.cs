using System.Security.Cryptography;
using System.Text;
using MimeKit;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class PostfixRawMimeInboundReplyParser : IInboundReplyMimeParser
{
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
            var from = mime.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
            var to = rawMessage.EnvelopeRecipient ?? mime.To.Mailboxes.FirstOrDefault()?.Address ?? mime.Cc.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                return Task.FromResult(EmailProviderInboundParseResult.Failure("Не удалось определить отправителя или получателя входящего ответа."));
            }

            var headers = mime.Headers
                .GroupBy(x => x.Field, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => string.Join("\n", x.Select(y => y.Value)), StringComparer.OrdinalIgnoreCase);
            var eventItem = new EmailProviderInboundEvent(
                Provider: "PostfixSpool",
                ProviderInboundEventId: BuildProviderInboundEventId(mime, rawMessage.SourceId, rawMessage.RawMime),
                FromEmail: from.Trim().ToLowerInvariant(),
                ToAddress: to.Trim().ToLowerInvariant(),
                ReplyToken: null,
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

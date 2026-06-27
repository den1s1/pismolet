using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record InboundReplyTokenOptions(string Secret, string InboundDomain, TimeSpan Lifetime)
{
    public static InboundReplyTokenOptions DevelopmentDefault { get; } = new(
        "dev-inbound-reply-secret-change-in-production",
        "reply.localhost",
        TimeSpan.FromDays(180));
}

public sealed record InboundReplyOptions(int BodyRetentionDays, int MaxStoredBodyChars, int ForwardBatchSize)
{
    public static InboundReplyOptions Default { get; } = new(14, 12000, 50);
}

public sealed record InboundReplyTokenPayload(
    string Version,
    string Purpose,
    Guid MailingId,
    string ClientId,
    string RecipientEmail,
    string RecipientKey,
    long ExpiresAtUnixSeconds);

public sealed record InboundReplyTokenValidationResult(bool Ok, string Error, InboundReplyTokenPayload? Payload)
{
    public static InboundReplyTokenValidationResult Success(InboundReplyTokenPayload payload) => new(true, string.Empty, payload);

    public static InboundReplyTokenValidationResult Failure(string error) => new(false, error, null);
}

public interface IInboundReplyTokenService
{
    string Generate(Guid mailingId, string clientId, string recipientEmail);

    InboundReplyTokenValidationResult Validate(string token);

    string BuildRecipientKey(Guid mailingId, string normalizedEmail);

    string BuildReplyToAddress(string token);

    string HashToken(string? token);
}

public sealed class SignedInboundReplyTokenService(IEmailNormalizer normalizer, InboundReplyTokenOptions options) : IInboundReplyTokenService
{
    private const string Version = "v1";
    private const string Purpose = "inbound_reply";

    public string Generate(Guid mailingId, string clientId, string recipientEmail)
    {
        var normalizedClient = normalizer.Normalize(clientId);
        var normalizedEmail = normalizer.Normalize(recipientEmail);
        if (string.IsNullOrWhiteSpace(normalizedClient) || string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException("Client and recipient are required for inbound reply token.");
        }

        var payload = new InboundReplyTokenPayload(
            Version,
            Purpose,
            mailingId,
            normalizedClient,
            normalizedEmail,
            BuildRecipientKey(mailingId, normalizedEmail),
            DateTimeOffset.UtcNow.Add(options.Lifetime).ToUnixTimeSeconds());

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signaturePart = Base64UrlEncode(Sign(payloadPart));
        return $"{Version}.{payloadPart}.{signaturePart}";
    }

    public InboundReplyTokenValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return InboundReplyTokenValidationResult.Failure("missing");
        }

        var parts = token.Trim().Split('.', 3);
        if (parts.Length != 3 || parts[0] != Version)
        {
            return InboundReplyTokenValidationResult.Failure("invalid");
        }

        var expected = Base64UrlEncode(Sign(parts[1]));
        if (!FixedTimeEquals(expected, parts[2]))
        {
            return InboundReplyTokenValidationResult.Failure("invalid_signature");
        }

        InboundReplyTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InboundReplyTokenPayload>(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
        }
        catch
        {
            return InboundReplyTokenValidationResult.Failure("invalid_payload");
        }

        if (payload is null || payload.Version != Version || payload.Purpose != Purpose)
        {
            return InboundReplyTokenValidationResult.Failure("invalid_purpose");
        }

        if (payload.ExpiresAtUnixSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return InboundReplyTokenValidationResult.Failure("expired");
        }

        var normalizedClient = normalizer.Normalize(payload.ClientId);
        var normalizedEmail = normalizer.Normalize(payload.RecipientEmail);
        var expectedRecipientKey = BuildRecipientKey(payload.MailingId, normalizedEmail);
        if (string.IsNullOrWhiteSpace(normalizedClient) || string.IsNullOrWhiteSpace(normalizedEmail) || !FixedTimeEquals(expectedRecipientKey, payload.RecipientKey))
        {
            return InboundReplyTokenValidationResult.Failure("recipient_mismatch");
        }

        return InboundReplyTokenValidationResult.Success(payload with { ClientId = normalizedClient, RecipientEmail = normalizedEmail });
    }

    public string BuildRecipientKey(Guid mailingId, string normalizedEmail) => Hash($"{mailingId:N}:{normalizer.Normalize(normalizedEmail)}");

    public string BuildReplyToAddress(string token) => $"reply+{token}{Convert.ToChar(64)}{options.InboundDomain.Trim().ToLowerInvariant()}";

    public string HashToken(string? token) => string.IsNullOrWhiteSpace(token) ? string.Empty : Hash(token.Trim());

    private byte[] Sign(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.Secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}

public sealed record InboundReplyMatchResult(bool Matched, string ErrorCode, string ErrorMessage, Mailing? Mailing, string? RecipientEmail)
{
    public static InboundReplyMatchResult Success(Mailing mailing, string recipientEmail) => new(true, string.Empty, string.Empty, mailing, recipientEmail);

    public static InboundReplyMatchResult Failure(string code, string message) => new(false, code, message, null, null);
}

public interface IInboundReplyMatchingService
{
    InboundReplyMatchResult Match(EmailProviderInboundEvent inbound);
}

public sealed class InboundReplyMatchingService(
    IInboundReplyTokenService tokens,
    IMailingRepository mailings,
    IEmailNormalizer normalizer) : IInboundReplyMatchingService
{
    public InboundReplyMatchResult Match(EmailProviderInboundEvent inbound)
    {
        var token = inbound.ReplyToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            token = ExtractTokenFromAddress(inbound.ToAddress);
        }

        var validation = tokens.Validate(token ?? string.Empty);
        if (!validation.Ok || validation.Payload is null)
        {
            return InboundReplyMatchResult.Failure("reply_token_invalid", "Reply token не прошёл проверку.");
        }

        var mailing = mailings.Get(validation.Payload.MailingId);
        if (mailing is null)
        {
            return InboundReplyMatchResult.Failure("mailing_not_found", "Рассылка не найдена.");
        }

        var owner = normalizer.Normalize(mailing.OwnerEmail);
        if (!string.Equals(owner, validation.Payload.ClientId, StringComparison.OrdinalIgnoreCase))
        {
            return InboundReplyMatchResult.Failure("client_mismatch", "Рассылка не принадлежит клиенту из reply token.");
        }

        var recipient = mailing.Recipients.FirstOrDefault(x =>
            x.Status == RecipientStatus.Accepted &&
            string.Equals(normalizer.Normalize(x.Email), validation.Payload.RecipientEmail, StringComparison.OrdinalIgnoreCase));
        if (recipient is null)
        {
            return InboundReplyMatchResult.Failure("recipient_not_found", "Получатель не найден в рассылке.");
        }

        var expectedKey = tokens.BuildRecipientKey(mailing.Id, validation.Payload.RecipientEmail);
        if (!string.Equals(expectedKey, validation.Payload.RecipientKey, StringComparison.OrdinalIgnoreCase))
        {
            return InboundReplyMatchResult.Failure("recipient_key_mismatch", "Ключ получателя не совпадает.");
        }

        return InboundReplyMatchResult.Success(mailing, validation.Payload.RecipientEmail);
    }

    private static string? ExtractTokenFromAddress(string? toAddress)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
        {
            return null;
        }

        var value = toAddress.Trim().Trim('<', '>', ' ', '\t', '\r', '\n');
        var at = value.IndexOf(Convert.ToChar(64));
        if (at <= 0)
        {
            return null;
        }

        var localPart = value[..at];
        if (localPart.StartsWith("reply+", StringComparison.OrdinalIgnoreCase) && localPart.Length > "reply+".Length)
        {
            return localPart["reply+".Length..];
        }

        return localPart.IndexOf('+') >= 0 ? null : localPart;
    }
}

public sealed record InboundReplyProcessResult(string Status, Guid CorrelationId, Guid? ReplyEventId);

public interface IBackgroundReplyQueue
{
    void EnqueueForward(Guid replyEventId);

    void EnqueueCleanup();
}

public interface IInboundReplyProcessingService
{
    Task<InboundReplyProcessResult> ProcessAsync(EmailProviderInboundEvent inbound, RequestMetadata request, CancellationToken cancellationToken);

    Task ExecuteForwardAsync(Guid replyEventId, CancellationToken cancellationToken);

    Task<int> CleanupExpiredBodiesAsync(CancellationToken cancellationToken);
}

public sealed class InboundReplyProcessingService(
    IReplyEventRepository replies,
    IInboundReplyMatchingService matcher,
    IInboundReplyTokenService tokens,
    IBackgroundReplyQueue queue,
    IEmailProviderAdapter provider,
    IAuditLogger audit,
    InboundReplyOptions options) : IInboundReplyProcessingService
{
    public Task<InboundReplyProcessResult> ProcessAsync(EmailProviderInboundEvent inbound, RequestMetadata request, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var existing = replies.GetByProviderEventId(inbound.Provider, inbound.ProviderInboundEventId);
        if (existing is not null)
        {
            Audit("inbound_reply_duplicate", $"replyEventId={existing.Id};provider={inbound.Provider}", request);
            return Task.FromResult(new InboundReplyProcessResult("duplicate", correlationId, existing.Id));
        }

        var body = Truncate(inbound.TextBody ?? inbound.HtmlBody ?? string.Empty, Math.Max(0, options.MaxStoredBodyChars));
        var receivedAt = inbound.ReceivedAt == default ? DateTimeOffset.UtcNow : inbound.ReceivedAt.ToUniversalTime();
        var reply = ReplyEvent.Received(
            inbound.Provider,
            inbound.ProviderInboundEventId,
            inbound.FromEmail,
            inbound.ToAddress,
            tokens.HashToken(inbound.ReplyToken),
            SafeSubject(inbound.Subject),
            body,
            receivedAt,
            receivedAt.AddDays(Math.Clamp(options.BodyRetentionDays, 1, 60)),
            Hash(inbound.RawPayload));

        if (InboundReplyAutoReplyDetector.ShouldIgnore(inbound))
        {
            reply = replies.AddIfNotExists(reply.MarkAutoReply("Обнаружены признаки auto-reply/mail loop."));
            Audit("inbound_reply_auto_ignored", $"replyEventId={reply.Id};provider={inbound.Provider}", request);
            return Task.FromResult(new InboundReplyProcessResult("ignored_auto_reply", correlationId, reply.Id));
        }

        var match = matcher.Match(inbound);
        if (!match.Matched || match.Mailing is null || string.IsNullOrWhiteSpace(match.RecipientEmail))
        {
            reply = replies.AddIfNotExists(reply.MarkUnmatched(match.ErrorCode, match.ErrorMessage));
            Audit("inbound_reply_unmatched", $"replyEventId={reply.Id};provider={inbound.Provider};error={match.ErrorCode}", request);
            return Task.FromResult(new InboundReplyProcessResult("unmatched", correlationId, reply.Id));
        }

        reply = reply
            .MarkMatched(match.Mailing.Id, match.Mailing.OwnerEmail, match.RecipientEmail, match.Mailing.OwnerEmail)
            .MarkQueuedForForward();
        reply = replies.AddIfNotExists(reply);
        queue.EnqueueForward(reply.Id);
        Audit("inbound_reply_queued", $"replyEventId={reply.Id};mailingId={match.Mailing.Id};emailHash={Hash(match.RecipientEmail)}", request);
        return Task.FromResult(new InboundReplyProcessResult("queued_for_forward", correlationId, reply.Id));
    }

    public async Task ExecuteForwardAsync(Guid replyEventId, CancellationToken cancellationToken)
    {
        var reply = replies.TryClaimForward(replyEventId);
        if (reply is null)
        {
            return;
        }

        if (reply.MailingId is null || string.IsNullOrWhiteSpace(reply.ForwardToEmailNormalized))
        {
            replies.MarkForwardFailed(replyEventId, "not_matched", "ReplyEvent не сопоставлен с рассылкой.");
            return;
        }

        EmailProviderSendResult result;
        try
        {
            result = await provider.ForwardReplyToClientAsync(reply, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            replies.MarkForwardQueued(replyEventId);
            return;
        }
        catch (Exception ex)
        {
            replies.MarkForwardFailed(replyEventId, "forward_exception", ex.Message);
            return;
        }

        if (result.Accepted)
        {
            replies.MarkForwarded(replyEventId);
        }
        else
        {
            replies.MarkForwardFailed(replyEventId, result.ErrorCode ?? "forward_failed", result.ErrorMessage ?? "Не удалось переслать ответ клиенту.");
        }
    }

    public Task<int> CleanupExpiredBodiesAsync(CancellationToken cancellationToken)
    {
        var expired = replies.FindExpiredBodies(DateTimeOffset.UtcNow, Math.Max(1, options.ForwardBatchSize));
        var count = 0;
        foreach (var item in expired)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            replies.MarkBodyDeleted(item.Id);
            count++;
        }

        return Task.FromResult(count);
    }

    private static string SafeSubject(string? subject)
    {
        var value = string.IsNullOrWhiteSpace(subject) ? "Без темы" : subject.Trim();
        return value.Length <= 160 ? value : value[..160];
    }

    private static string Truncate(string value, int max)
    {
        if (max <= 0 || string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = value.Trim();
        return clean.Length <= max ? clean : clean[..max] + "\n\n[Ответ обрезан по политике хранения MVP]";
    }

    private void Audit(string eventType, string context, RequestMetadata request) => audit.Write(new AuditRecord(DateTimeOffset.UtcNow, "inbound-reply", eventType, request.Ip, request.UserAgent, context));

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

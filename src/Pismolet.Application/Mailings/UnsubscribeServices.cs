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

public sealed record UnsubscribeTokenOptions(string Secret, TimeSpan Lifetime)
{
    public static UnsubscribeTokenOptions DevelopmentDefault { get; } = new("dev-unsubscribe-secret-change-in-production", TimeSpan.FromDays(90));
}

public sealed record UnsubscribeTokenPayload(
    string Version,
    string Purpose,
    Guid MailingId,
    string Email,
    string RecipientKey,
    long ExpiresAtUnixSeconds);

public sealed record UnsubscribeTokenValidationResult(bool Ok, string Error, UnsubscribeTokenPayload? Payload)
{
    public static UnsubscribeTokenValidationResult Success(UnsubscribeTokenPayload payload) => new(true, string.Empty, payload);

    public static UnsubscribeTokenValidationResult Failure(string error) => new(false, error, null);
}

public interface IUnsubscribeTokenService
{
    string Generate(Guid mailingId, string recipientEmail, Guid? importBatchId = null);

    UnsubscribeTokenValidationResult Validate(string token);

    string BuildRecipientKey(Guid mailingId, string normalizedEmail, Guid? importBatchId = null);
}

public sealed class SignedUnsubscribeTokenService(IEmailNormalizer normalizer, UnsubscribeTokenOptions options) : IUnsubscribeTokenService
{
    private const string Version = "v1";
    private const string Purpose = "global_unsubscribe";

    public string Generate(Guid mailingId, string recipientEmail, Guid? importBatchId = null)
    {
        var email = normalizer.Normalize(recipientEmail);
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(recipientEmail));
        }

        var payload = new UnsubscribeTokenPayload(
            Version,
            Purpose,
            mailingId,
            email,
            BuildRecipientKey(mailingId, email, importBatchId),
            DateTimeOffset.UtcNow.Add(options.Lifetime).ToUnixTimeSeconds());

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signaturePart = Base64UrlEncode(Sign(payloadPart));
        return $"{Version}.{payloadPart}.{signaturePart}";
    }

    public UnsubscribeTokenValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return UnsubscribeTokenValidationResult.Failure("invalid");
        }

        var parts = token.Split('.', 3);
        if (parts.Length != 3 || parts[0] != Version)
        {
            return UnsubscribeTokenValidationResult.Failure("invalid");
        }

        var expected = Base64UrlEncode(Sign(parts[1]));
        if (!FixedTimeEquals(expected, parts[2]))
        {
            return UnsubscribeTokenValidationResult.Failure("invalid_signature");
        }

        UnsubscribeTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UnsubscribeTokenPayload>(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
        }
        catch
        {
            return UnsubscribeTokenValidationResult.Failure("invalid_payload");
        }

        if (payload is null || payload.Version != Version || payload.Purpose != Purpose || string.IsNullOrWhiteSpace(payload.Email))
        {
            return UnsubscribeTokenValidationResult.Failure("invalid_payload");
        }

        if (payload.ExpiresAtUnixSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return UnsubscribeTokenValidationResult.Failure("expired");
        }

        var normalized = normalizer.Normalize(payload.Email);
        var expectedRecipientKey = BuildRecipientKey(payload.MailingId, normalized);
        if (!FixedTimeEquals(expectedRecipientKey, payload.RecipientKey))
        {
            return UnsubscribeTokenValidationResult.Failure("recipient_mismatch");
        }

        return UnsubscribeTokenValidationResult.Success(payload with { Email = normalized });
    }

    public string BuildRecipientKey(Guid mailingId, string normalizedEmail, Guid? importBatchId = null)
    {
        // В текущей модели Recipient нет Id. Для token используем стабильный ключ:
        // mailingId + normalizedEmail + optional importBatchId. Это не требует несуществующего поля Recipient.Id.
        var raw = importBatchId is null
            ? $"{mailingId:N}:{normalizer.Normalize(normalizedEmail)}"
            : $"{mailingId:N}:{importBatchId:N}:{normalizer.Normalize(normalizedEmail)}";
        return Hash(raw);
    }

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

public sealed record UnsubscribeViewResult(bool TokenValid, string MaskedEmail, string Error);

public sealed record UnsubscribeConfirmResult(bool Ok, bool AlreadySuppressed, string Message);

public interface IGlobalUnsubscribeService
{
    UnsubscribeViewResult GetView(string token, RequestMetadata request);

    UnsubscribeConfirmResult Confirm(string token, RequestMetadata request);
}

public sealed class GlobalUnsubscribeService(
    IUnsubscribeTokenService tokens,
    IGlobalSuppressionRepository suppressions,
    ISendEventRepository sendEvents,
    IEmailNormalizer normalizer,
    IAuditLogger audit) : IGlobalUnsubscribeService
{
    public UnsubscribeViewResult GetView(string token, RequestMetadata request)
    {
        var validation = tokens.Validate(token);
        if (!validation.Ok || validation.Payload is null)
        {
            Audit("unsubscribe_token_invalid", "error=" + validation.Error, request);
            return new UnsubscribeViewResult(false, string.Empty, "Ссылка отписки недействительна или устарела.");
        }

        return new UnsubscribeViewResult(true, MaskEmail(validation.Payload.Email), string.Empty);
    }

    public UnsubscribeConfirmResult Confirm(string token, RequestMetadata request)
    {
        var validation = tokens.Validate(token);
        if (!validation.Ok || validation.Payload is null)
        {
            Audit("unsubscribe_token_invalid", "error=" + validation.Error, request);
            return new UnsubscribeConfirmResult(false, false, "Ссылка отписки недействительна или устарела.");
        }

        var email = normalizer.Normalize(validation.Payload.Email);
        var existed = suppressions.IsSuppressed(email);
        var suppression = GlobalSuppression.FromUnsubscribeLink(
            email,
            Hash(email),
            validation.Payload.MailingId,
            validation.Payload.RecipientKey,
            Hash(request.Ip),
            Hash(request.UserAgent));

        suppressions.AddOrGet(suppression);
        MarkCurrentMailingUnsubscribe(validation.Payload.MailingId, email);
        Audit(existed ? "unsubscribe_repeated" : "unsubscribe_confirmed", $"emailHash={Hash(email)};mailingId={validation.Payload.MailingId};recipientKey={validation.Payload.RecipientKey}", request);

        return new UnsubscribeConfirmResult(true, existed, "Вы отписаны от писем через сервис. Этот адрес больше не будет получать рассылки через Письмолёт.");
    }

    private void MarkCurrentMailingUnsubscribe(Guid mailingId, string email)
    {
        var sendEvent = sendEvents.Get(mailingId, email);
        if (sendEvent is null)
        {
            return;
        }

        sendEvents.Save(sendEvent.MarkUnsubscribed());
    }

    private void Audit(string eventType, string context, RequestMetadata request) => audit.Write(new AuditRecord(
        DateTimeOffset.UtcNow,
        "public-unsubscribe",
        eventType,
        request.Ip,
        request.UserAgent,
        context));

    private static string Hash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "адрес скрыт" : $"{email[..1]}***{email[at..]}";
    }
}

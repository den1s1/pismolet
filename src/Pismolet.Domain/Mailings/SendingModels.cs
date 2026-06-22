using System.Security.Cryptography;
using System.Text;

namespace Pismolet.Web.Domain.Mailings;

public enum SendEventStatus
{
    Pending,
    Accepted,
    Failed,
    Skipped,
    Paused
}

public enum SendSkipReason
{
    None,
    GlobalSuppression,
    ClientSuppression,
    DailyLimit,
    WarmupLimit,
    AlreadySent,
    NoMessage
}

public enum DeliveryStatus
{
    NotReported,
    Accepted,
    Delivered,
    SoftBounce,
    HardBounce,
    Complaint,
    Rejected,
    Unknown
}

public enum ProviderWebhookEventType
{
    Accepted,
    Delivered,
    SoftBounce,
    HardBounce,
    Complaint,
    Rejected,
    Unknown
}

public enum ProviderWebhookProcessingStatus
{
    Processed,
    IgnoredDuplicate,
    IgnoredUnknown,
    Unmatched,
    Failed
}

public enum ClientSuppressionReason
{
    HardBounce,
    ManualBlock
}

public static class SendEventStatusLabels
{
    public static string ToRu(this SendEventStatus status) => status switch
    {
        SendEventStatus.Accepted => "Отправлено",
        SendEventStatus.Failed => "Ошибка",
        SendEventStatus.Skipped => "Исключено",
        SendEventStatus.Paused => "Приостановлено",
        _ => "Ожидает отправки"
    };
}

public static class DeliveryStatusLabels
{
    public static string ToRu(this DeliveryStatus status) => status switch
    {
        DeliveryStatus.Accepted => "Принято провайдером",
        DeliveryStatus.Delivered => "Доставлено",
        DeliveryStatus.SoftBounce => "Временная ошибка",
        DeliveryStatus.HardBounce => "Постоянная ошибка",
        DeliveryStatus.Complaint => "Жалоба",
        DeliveryStatus.Rejected => "Отклонено",
        DeliveryStatus.Unknown => "Неизвестное событие",
        _ => "Ожидаем статус доставки"
    };

    public static DeliveryStatus FromEventType(ProviderWebhookEventType eventType) => eventType switch
    {
        ProviderWebhookEventType.Accepted => DeliveryStatus.Accepted,
        ProviderWebhookEventType.Delivered => DeliveryStatus.Delivered,
        ProviderWebhookEventType.SoftBounce => DeliveryStatus.SoftBounce,
        ProviderWebhookEventType.HardBounce => DeliveryStatus.HardBounce,
        ProviderWebhookEventType.Complaint => DeliveryStatus.Complaint,
        ProviderWebhookEventType.Rejected => DeliveryStatus.Rejected,
        ProviderWebhookEventType.Unknown => DeliveryStatus.Unknown,
        _ => DeliveryStatus.Unknown
    };
}

public sealed record SendEvent(
    Guid Id,
    Guid MailingId,
    string OwnerEmail,
    string RecipientEmail,
    SendEventStatus Status,
    SendSkipReason? Reason,
    string Provider,
    string? ProviderMessageId,
    int Attempt,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DeliveryStatus DeliveryStatus = DeliveryStatus.NotReported,
    DateTimeOffset? LastDeliveryEventAt = null,
    string? LastDeliverySummary = null,
    DateTimeOffset? AcceptedAt = null,
    string? TrackingToken = null,
    DateTimeOffset? FirstOpenedAt = null,
    DateTimeOffset? LastOpenedAt = null,
    int OpenCount = 0)
{
    public const string FakeProvider = "FakeEmail";
    public const string ProviderEnvelopeSeparator = "::";

    public static SendEvent Pending(Guid mailingId, string ownerEmail, string recipientEmail) => new(
        Guid.NewGuid(),
        mailingId,
        ownerEmail.Trim().ToLowerInvariant(),
        recipientEmail.Trim().ToLowerInvariant(),
        SendEventStatus.Pending,
        SendSkipReason.None,
        FakeProvider,
        null,
        0,
        null,
        null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        TrackingToken: BuildTrackingToken(mailingId, recipientEmail));

    public SendEvent MarkPaused(SendSkipReason reason) => this with
    {
        Status = SendEventStatus.Paused,
        Reason = reason,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public SendEvent MarkSkipped(SendSkipReason reason) => this with
    {
        Status = SendEventStatus.Skipped,
        Reason = reason,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public SendEvent MarkAccepted(string providerMessageId)
    {
        var now = DateTimeOffset.UtcNow;
        var resolved = ResolveProviderEnvelope(providerMessageId);
        return this with
        {
            Status = SendEventStatus.Accepted,
            Reason = SendSkipReason.None,
            Provider = resolved.Provider ?? Provider,
            ProviderMessageId = string.IsNullOrWhiteSpace(ProviderMessageId) ? resolved.Payload : ProviderMessageId,
            Attempt = Attempt + 1,
            ErrorCode = null,
            ErrorMessage = null,
            UpdatedAt = now,
            AcceptedAt = AcceptedAt ?? now,
            TrackingToken = string.IsNullOrWhiteSpace(TrackingToken) ? BuildTrackingToken(MailingId, RecipientEmail) : TrackingToken
        };
    }

    public SendEvent MarkFailed(string errorCode, string errorMessage)
    {
        var resolved = ResolveProviderEnvelope(errorCode);
        return this with
        {
            Status = SendEventStatus.Failed,
            Reason = SendSkipReason.None,
            Provider = resolved.Provider ?? Provider,
            Attempt = Attempt + 1,
            ErrorCode = resolved.Payload,
            ErrorMessage = errorMessage,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public SendEvent MarkOpened(DateTimeOffset openedAt)
    {
        var openedAtUtc = openedAt.ToUniversalTime();
        return this with
        {
            FirstOpenedAt = FirstOpenedAt ?? openedAtUtc,
            LastOpenedAt = openedAtUtc,
            OpenCount = OpenCount + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public SendEvent ApplyDeliveryStatus(DeliveryStatus nextStatus, DateTimeOffset occurredAt, string? summary)
    {
        if (nextStatus == DeliveryStatus.Unknown)
        {
            return this;
        }

        if (Priority(nextStatus) < Priority(DeliveryStatus))
        {
            return this;
        }

        return this with
        {
            DeliveryStatus = nextStatus,
            LastDeliveryEventAt = occurredAt,
            LastDeliverySummary = summary,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public SendEvent ResetForResume() => Status == SendEventStatus.Paused && Reason is SendSkipReason.DailyLimit or SendSkipReason.WarmupLimit
        ? this with { Status = SendEventStatus.Pending, Reason = SendSkipReason.None, UpdatedAt = DateTimeOffset.UtcNow }
        : this;

    public static string NewTrackingToken() => Guid.NewGuid().ToString("N");

    public static string BuildTrackingToken(Guid mailingId, string recipientEmail)
    {
        var normalizedRecipient = recipientEmail.Trim().ToLowerInvariant();
        var raw = $"pismolet-open-tracking-v1:{mailingId:N}:{normalizedRecipient}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static (string? Provider, string Payload) ResolveProviderEnvelope(string payload)
    {
        var trimmed = payload.Trim();
        var separatorIndex = trimmed.IndexOf(ProviderEnvelopeSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return (null, trimmed);
        }

        var provider = trimmed[..separatorIndex].Trim();
        var innerPayload = trimmed[(separatorIndex + ProviderEnvelopeSeparator.Length)..].Trim();
        return string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(innerPayload)
            ? (null, trimmed)
            : (provider, innerPayload);
    }

    private static int Priority(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Complaint => 70,
        DeliveryStatus.HardBounce => 60,
        DeliveryStatus.Rejected => 55,
        DeliveryStatus.Delivered => 50,
        DeliveryStatus.SoftBounce => 40,
        DeliveryStatus.Accepted => 30,
        DeliveryStatus.Unknown => 10,
        _ => 0
    };
}

public sealed record ProviderWebhookEvent(
    Guid Id,
    string Provider,
    string ProviderEventId,
    string? ProviderMessageId,
    Guid? MailingId,
    string? RecipientEmail,
    ProviderWebhookEventType EventType,
    DateTimeOffset OccurredAt,
    string? ReasonCode,
    string? ReasonMessage,
    ProviderWebhookProcessingStatus ProcessingStatus,
    string RawPayload,
    DateTimeOffset CreatedAt)
{
    public static ProviderWebhookEvent New(
        string provider,
        string providerEventId,
        string? providerMessageId,
        Guid? mailingId,
        string? recipientEmail,
        ProviderWebhookEventType eventType,
        DateTimeOffset occurredAt,
        string? reasonCode,
        string? reasonMessage,
        string rawPayload) => new(
        Guid.NewGuid(),
        provider.Trim(),
        providerEventId.Trim(),
        string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId.Trim(),
        mailingId,
        string.IsNullOrWhiteSpace(recipientEmail) ? null : recipientEmail.Trim().ToLowerInvariant(),
        eventType,
        occurredAt.ToUniversalTime(),
        string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim(),
        string.IsNullOrWhiteSpace(reasonMessage) ? null : reasonMessage.Trim(),
        ProviderWebhookProcessingStatus.Unmatched,
        rawPayload,
        DateTimeOffset.UtcNow);

    public ProviderWebhookEvent MarkProcessed() => this with { ProcessingStatus = ProviderWebhookProcessingStatus.Processed };

    public ProviderWebhookEvent MarkIgnoredDuplicate() => this with { ProcessingStatus = ProviderWebhookProcessingStatus.IgnoredDuplicate };

    public ProviderWebhookEvent MarkIgnoredUnknown() => this with { ProcessingStatus = ProviderWebhookProcessingStatus.IgnoredUnknown };

    public ProviderWebhookEvent MarkUnmatched() => this with { ProcessingStatus = ProviderWebhookProcessingStatus.Unmatched };

    public ProviderWebhookEvent MarkFailed() => this with { ProcessingStatus = ProviderWebhookProcessingStatus.Failed };
}

public sealed record ClientSuppression(
    Guid Id,
    string OwnerEmail,
    string RecipientEmail,
    ClientSuppressionReason Reason,
    string Source,
    DateTimeOffset CreatedAt)
{
    public static ClientSuppression New(string ownerEmail, string recipientEmail, ClientSuppressionReason reason, string source) => new(
        Guid.NewGuid(),
        ownerEmail.Trim().ToLowerInvariant(),
        recipientEmail.Trim().ToLowerInvariant(),
        reason,
        source.Trim(),
        DateTimeOffset.UtcNow);
}

public sealed record MailingSendSummary(
    int TotalAcceptedRecipients,
    int AcceptedForSending,
    int Pending,
    int Sent,
    int Failed,
    int Suppressed,
    int ClientSuppressed,
    int PausedByLimit);

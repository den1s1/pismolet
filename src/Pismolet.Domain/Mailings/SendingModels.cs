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
    DateTimeOffset? AcceptedAt = null)
{
    public const string FakeProvider = "FakeEmail";

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
        DateTimeOffset.UtcNow);

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
        return this with
        {
            Status = SendEventStatus.Accepted,
            Reason = SendSkipReason.None,
            ProviderMessageId = string.IsNullOrWhiteSpace(ProviderMessageId) ? providerMessageId : ProviderMessageId,
            Attempt = Attempt + 1,
            ErrorCode = null,
            ErrorMessage = null,
            UpdatedAt = now,
            AcceptedAt = AcceptedAt ?? now
        };
    }

    public SendEvent MarkFailed(string errorCode, string errorMessage) => this with
    {
        Status = SendEventStatus.Failed,
        Reason = SendSkipReason.None,
        Attempt = Attempt + 1,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        UpdatedAt = DateTimeOffset.UtcNow
    };

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

    public SendEvent ResetForResume() => Status == SendEventStatus.Paused && Reason == SendSkipReason.DailyLimit
        ? this with { Status = SendEventStatus.Pending, Reason = SendSkipReason.None, UpdatedAt = DateTimeOffset.UtcNow }
        : this;

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
    string? ClientId,
    string? RecipientEmailNormalized,
    ProviderWebhookEventType EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string RawPayloadHash,
    string? RawPayloadStored,
    string? ReasonCode,
    string? ReasonMessage,
    ProviderWebhookProcessingStatus ProcessingStatus,
    Guid CorrelationId)
{
    public ProviderWebhookEvent WithProcessingStatus(ProviderWebhookProcessingStatus status) => this with { ProcessingStatus = status };
}

public sealed record ClientSuppression(
    Guid Id,
    string ClientId,
    string EmailNormalized,
    ClientSuppressionReason Reason,
    Guid? SourceMailingId,
    string? SourceProviderMessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt)
{
    public static ClientSuppression FromHardBounce(string clientId, string emailNormalized, Guid? sourceMailingId, string? providerMessageId) => new(
        Guid.NewGuid(),
        clientId.Trim().ToLowerInvariant(),
        emailNormalized.Trim().ToLowerInvariant(),
        ClientSuppressionReason.HardBounce,
        sourceMailingId,
        providerMessageId,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    public ClientSuppression Touch(Guid? sourceMailingId, string? providerMessageId) => this with
    {
        SourceMailingId = SourceMailingId ?? sourceMailingId,
        SourceProviderMessageId = SourceProviderMessageId ?? providerMessageId,
        LastSeenAt = DateTimeOffset.UtcNow
    };
}

public sealed record MailingSendSummary(
    Guid MailingId,
    int AcceptedForSending,
    int Sent,
    int Failed,
    int Suppressed,
    int ClientSuppressed,
    int PausedByLimit,
    int SkippedOther,
    int Pending,
    int TotalAcceptedRecipients,
    int ProviderAccepted = 0,
    int Delivered = 0,
    int SoftBounced = 0,
    int HardBounced = 0,
    int Complaints = 0,
    int Rejected = 0,
    int UnknownDelivery = 0)
{
    public static MailingSendSummary Empty(Guid mailingId, int totalAcceptedRecipients = 0) => new(mailingId, 0, 0, 0, 0, 0, 0, 0, 0, totalAcceptedRecipients);
}

namespace Pismolet.Web.Domain.Mailings;

public enum ReplyProcessingStatus
{
    Received,
    Matched,
    QueuedForForward,
    Forwarded,
    Unmatched,
    IgnoredAutoReply,
    Duplicate,
    Failed
}

public enum ReplyBodyStorageStatus
{
    StoredTemporarily,
    Redacted,
    Deleted,
    NotStored
}

public static class ReplyProcessingStatusLabels
{
    public static string ToRu(this ReplyProcessingStatus status) => status switch
    {
        ReplyProcessingStatus.Received => "Получен",
        ReplyProcessingStatus.Matched => "Сопоставлен",
        ReplyProcessingStatus.QueuedForForward => "Ожидает пересылки клиенту",
        ReplyProcessingStatus.Forwarded => "Переслан клиенту",
        ReplyProcessingStatus.Unmatched => "Не сопоставлен",
        ReplyProcessingStatus.IgnoredAutoReply => "Автоответ не пересылался",
        ReplyProcessingStatus.Duplicate => "Повторное событие",
        ReplyProcessingStatus.Failed => "Ошибка обработки",
        _ => "Неизвестно"
    };
}

public sealed record ReplyEvent(
    Guid Id,
    string Provider,
    string ProviderInboundEventId,
    Guid? MailingId,
    string? ClientId,
    string? RecipientEmailNormalized,
    string FromEmailNormalized,
    string ToAddress,
    string? ReplyTokenHash,
    string SubjectPreview,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? ForwardQueuedAt,
    DateTimeOffset? ForwardedAt,
    string? ForwardToEmailNormalized,
    ReplyProcessingStatus ProcessingStatus,
    int ForwardRetryCount,
    ReplyBodyStorageStatus BodyStorageStatus,
    DateTimeOffset? BodyExpiresAt,
    string? BodyTextStored,
    string RawPayloadHash,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ReplyEvent Received(
        string provider,
        string providerInboundEventId,
        string fromEmail,
        string toAddress,
        string? replyTokenHash,
        string subjectPreview,
        string bodyTextStored,
        DateTimeOffset receivedAt,
        DateTimeOffset bodyExpiresAt,
        string rawPayloadHash) => new(
            Guid.NewGuid(),
            provider.Trim(),
            providerInboundEventId.Trim(),
            null,
            null,
            null,
            fromEmail.Trim().ToLowerInvariant(),
            toAddress.Trim().ToLowerInvariant(),
            replyTokenHash,
            subjectPreview,
            receivedAt.ToUniversalTime(),
            null,
            null,
            null,
            null,
            ReplyProcessingStatus.Received,
            0,
            string.IsNullOrWhiteSpace(bodyTextStored) ? ReplyBodyStorageStatus.NotStored : ReplyBodyStorageStatus.StoredTemporarily,
            string.IsNullOrWhiteSpace(bodyTextStored) ? null : bodyExpiresAt.ToUniversalTime(),
            string.IsNullOrWhiteSpace(bodyTextStored) ? null : bodyTextStored,
            rawPayloadHash,
            null,
            null);

    public ReplyEvent MarkMatched(Guid mailingId, string clientId, string recipientEmail, string forwardToEmail) => this with
    {
        MailingId = mailingId,
        ClientId = clientId.Trim().ToLowerInvariant(),
        RecipientEmailNormalized = recipientEmail.Trim().ToLowerInvariant(),
        ForwardToEmailNormalized = forwardToEmail.Trim().ToLowerInvariant(),
        ProcessingStatus = ReplyProcessingStatus.Matched,
        ProcessedAt = DateTimeOffset.UtcNow,
        ErrorCode = null,
        ErrorMessage = null
    };

    public ReplyEvent MarkQueuedForForward() => this with
    {
        ProcessingStatus = ReplyProcessingStatus.QueuedForForward,
        ForwardQueuedAt = ForwardQueuedAt ?? DateTimeOffset.UtcNow,
        ProcessedAt = ProcessedAt ?? DateTimeOffset.UtcNow
    };

    public ReplyEvent MarkUnmatched(string errorCode, string errorMessage) => this with
    {
        ProcessingStatus = ReplyProcessingStatus.Unmatched,
        ProcessedAt = DateTimeOffset.UtcNow,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };

    public ReplyEvent MarkAutoReply(string reason) => this with
    {
        ProcessingStatus = ReplyProcessingStatus.IgnoredAutoReply,
        ProcessedAt = DateTimeOffset.UtcNow,
        ErrorCode = "auto_reply",
        ErrorMessage = reason
    };

    public ReplyEvent MarkForwarded() => this with
    {
        ProcessingStatus = ReplyProcessingStatus.Forwarded,
        ForwardedAt = DateTimeOffset.UtcNow,
        ErrorCode = null,
        ErrorMessage = null
    };

    public ReplyEvent MarkForwardFailed(string errorCode, string errorMessage) => this with
    {
        ProcessingStatus = ReplyProcessingStatus.Failed,
        ForwardRetryCount = ForwardRetryCount + 1,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };

    public ReplyEvent MarkBodyDeleted() => this with
    {
        BodyStorageStatus = ReplyBodyStorageStatus.Deleted,
        BodyTextStored = null,
        BodyExpiresAt = null
    };
}

public sealed record ReplySummary(Guid MailingId, int TotalReplies, DateTimeOffset? LastReplyAt, ReplyProcessingStatus? LastStatus)
{
    public static ReplySummary Empty(Guid mailingId) => new(mailingId, 0, null, null);
}

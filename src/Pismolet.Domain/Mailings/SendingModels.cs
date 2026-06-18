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
    DailyLimit,
    AlreadySent,
    NoMessage
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

public sealed record SendEvent(
    Guid Id,
    Guid MailingId,
    string OwnerEmail,
    string RecipientEmail,
    SendEventStatus Status,
    SendSkipReason Reason,
    string Provider,
    string? ProviderMessageId,
    int Attempt,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
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

    public SendEvent MarkAccepted(string providerMessageId) => this with
    {
        Status = SendEventStatus.Accepted,
        Reason = SendSkipReason.None,
        ProviderMessageId = string.IsNullOrWhiteSpace(ProviderMessageId) ? providerMessageId : ProviderMessageId,
        Attempt = Attempt + 1,
        ErrorCode = null,
        ErrorMessage = null,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public SendEvent MarkFailed(string errorCode, string errorMessage) => this with
    {
        Status = SendEventStatus.Failed,
        Reason = SendSkipReason.None,
        Attempt = Attempt + 1,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public SendEvent ResetForResume() => Status == SendEventStatus.Paused && Reason == SendSkipReason.DailyLimit
        ? this with { Status = SendEventStatus.Pending, Reason = SendSkipReason.None, UpdatedAt = DateTimeOffset.UtcNow }
        : this;
}

public sealed record MailingSendSummary(
    Guid MailingId,
    int AcceptedForSending,
    int Sent,
    int Failed,
    int Suppressed,
    int PausedByLimit,
    int SkippedOther,
    int Pending,
    int TotalAcceptedRecipients)
{
    public static MailingSendSummary Empty(Guid mailingId, int totalAcceptedRecipients = 0) => new(mailingId, 0, 0, 0, 0, 0, 0, 0, totalAcceptedRecipients);
}

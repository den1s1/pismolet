namespace Pismolet.Web.Domain.Mailings;

public enum PostfixDeliveryEventStatus
{
    Sent,
    Deferred,
    Bounced,
    Expired,
    Unknown
}

public sealed record PostfixDeliveryEvent(
    Guid Id,
    string QueueId,
    string RecipientEmail,
    PostfixDeliveryEventStatus Status,
    DeliveryStatus DeliveryStatus,
    string? Dsn,
    string? Relay,
    string? Diagnostic,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt)
{
    public static PostfixDeliveryEvent FromParsed(
        string queueId,
        string recipientEmail,
        PostfixDeliveryEventStatus status,
        DeliveryStatus deliveryStatus,
        string? dsn,
        string? relay,
        string? diagnostic,
        DateTimeOffset occurredAt) => new(
            Guid.NewGuid(),
            NormalizeQueueId(queueId),
            NormalizeEmail(recipientEmail),
            status,
            deliveryStatus,
            NormalizeNullable(dsn),
            NormalizeNullable(relay),
            NormalizeNullable(diagnostic),
            occurredAt.ToUniversalTime(),
            DateTimeOffset.UtcNow);

    public static string NormalizeQueueId(string queueId) => queueId.Trim().ToUpperInvariant();

    public static string NormalizeEmail(string recipientEmail) => recipientEmail.Trim().ToLowerInvariant();

    public static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

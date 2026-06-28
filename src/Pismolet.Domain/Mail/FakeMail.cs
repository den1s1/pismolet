namespace Pismolet.Web.Domain.Mail;

public sealed record FakeMail(
    string To,
    string Subject,
    string Link,
    DateTimeOffset CreatedAt,
    string? ReplyToAddress = null,
    string? ReplyToken = null,
    string? ProviderMessageId = null,
    string? TextBody = null,
    string? FromEmail = null,
    bool IsForwardedReply = false,
    string? MessageId = null);

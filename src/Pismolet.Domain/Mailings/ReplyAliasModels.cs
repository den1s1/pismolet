namespace Pismolet.Web.Domain.Mailings;

public sealed record ClientReplyAlias(
    Guid Id,
    string ClientId,
    string Alias,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ClientReplyAlias Create(string clientId, string alias, DateTimeOffset? now = null)
    {
        var timestamp = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return new ClientReplyAlias(
            Guid.NewGuid(),
            clientId.Trim().ToLowerInvariant(),
            alias.Trim().ToLowerInvariant(),
            timestamp,
            timestamp);
    }
}

public sealed record OutboundReplyMessageMapping(
    Guid Id,
    string MessageId,
    Guid MailingId,
    Guid SendEventId,
    string ClientId,
    string RecipientEmailNormalized,
    string ReplyAlias,
    DateTimeOffset CreatedAt)
{
    public static OutboundReplyMessageMapping Create(
        string messageId,
        Guid mailingId,
        Guid sendEventId,
        string clientId,
        string recipientEmail,
        string replyAlias,
        DateTimeOffset? now = null) => new(
            Guid.NewGuid(),
            NormalizeMessageId(messageId),
            mailingId,
            sendEventId,
            clientId.Trim().ToLowerInvariant(),
            recipientEmail.Trim().ToLowerInvariant(),
            replyAlias.Trim().ToLowerInvariant(),
            (now ?? DateTimeOffset.UtcNow).ToUniversalTime());

    public static string NormalizeMessageId(string messageId)
    {
        var value = messageId.Trim();
        return value.StartsWith('<') && value.EndsWith('>')
            ? value.ToLowerInvariant()
            : $"<{value.Trim('<', '>')}>".ToLowerInvariant();
    }
}

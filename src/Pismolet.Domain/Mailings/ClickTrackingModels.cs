using System.Security.Cryptography;
using System.Text;

namespace Pismolet.Web.Domain.Mailings;

public sealed record TrackedLink(
    Guid Id,
    Guid MailingId,
    string RecipientEmail,
    string Token,
    string OriginalUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FirstClickedAt = null,
    DateTimeOffset? LastClickedAt = null,
    int ClickCount = 0)
{
    public static TrackedLink Create(Guid mailingId, string recipientEmail, string originalUrl)
    {
        var normalizedRecipient = NormalizeRecipient(recipientEmail);
        var normalizedUrl = NormalizeOriginalUrl(originalUrl);
        var now = DateTimeOffset.UtcNow;

        return new TrackedLink(
            Guid.NewGuid(),
            mailingId,
            normalizedRecipient,
            BuildToken(mailingId, normalizedRecipient, normalizedUrl),
            normalizedUrl,
            now,
            now);
    }

    public TrackedLink MarkClicked(DateTimeOffset clickedAt)
    {
        var clickedAtUtc = clickedAt.ToUniversalTime();
        return this with
        {
            FirstClickedAt = FirstClickedAt ?? clickedAtUtc,
            LastClickedAt = clickedAtUtc,
            ClickCount = ClickCount + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static string BuildToken(Guid mailingId, string recipientEmail, string originalUrl)
    {
        var normalizedRecipient = NormalizeRecipient(recipientEmail);
        var normalizedUrl = NormalizeOriginalUrl(originalUrl);
        var raw = $"pismolet-click-tracking-v1:{mailingId:N}:{normalizedRecipient}:{normalizedUrl}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string NormalizeRecipient(string recipientEmail) => recipientEmail.Trim().ToLowerInvariant();

    public static string NormalizeOriginalUrl(string originalUrl)
    {
        var trimmed = originalUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Для отслеживания переходов допустимы только абсолютные http/https ссылки.", nameof(originalUrl));
        }

        return uri.ToString();
    }
}

public sealed record ClickEvent(
    Guid Id,
    Guid TrackedLinkId,
    Guid MailingId,
    string RecipientEmail,
    string Token,
    string OriginalUrl,
    DateTimeOffset ClickedAt,
    string? IpHash,
    string? UserAgentHash)
{
    public static ClickEvent Create(TrackedLink trackedLink, DateTimeOffset clickedAt, string? ipHash, string? userAgentHash) => new(
        Guid.NewGuid(),
        trackedLink.Id,
        trackedLink.MailingId,
        trackedLink.RecipientEmail,
        trackedLink.Token,
        trackedLink.OriginalUrl,
        clickedAt.ToUniversalTime(),
        ipHash,
        userAgentHash);
}

using Microsoft.Extensions.Logging;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class DirectLinkSmtpEmailProviderAdapter(
    SmtpEmailProviderOptions options,
    PublicUrlOptions publicUrlOptions,
    ILogger<SmtpEmailProviderAdapter> logger,
    IClickTrackingRepository clickTracking) : IEmailProviderAdapter
{
    private readonly SmtpEmailProviderAdapter inner = new(options, publicUrlOptions, logger, clickTracking: null);

    public string ProviderName => inner.ProviderName;

    public Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        inner.SendAsync(PrepareMessage(message), cancellationToken);

    public Task<EmailProviderWebhookParseResult> ParseWebhookAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken) =>
        inner.ParseWebhookAsync(rawBody, headers, cancellationToken);

    public Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken) =>
        inner.ParseInboundWebhookAsync(rawBody, headers, cancellationToken);

    public Task<EmailProviderSendResult> ForwardReplyToClientAsync(
        ReplyEvent replyEvent,
        CancellationToken cancellationToken) =>
        inner.ForwardReplyToClientAsync(replyEvent, cancellationToken);

    private EmailMessage PrepareMessage(EmailMessage message)
    {
        var restoredBody = message.BodyFormat == MessageBodyFormat.Html
            ? LegacyClickTrackingLinkRestorer.RestoreHtml(message.PlainTextBody, ResolveOriginalUrl)
            : LegacyClickTrackingLinkRestorer.RestoreText(message.PlainTextBody, ResolveOriginalUrl);

        return message with { PlainTextBody = restoredBody };
    }

    private string? ResolveOriginalUrl(string token) => clickTracking.GetByToken(token)?.OriginalUrl;
}

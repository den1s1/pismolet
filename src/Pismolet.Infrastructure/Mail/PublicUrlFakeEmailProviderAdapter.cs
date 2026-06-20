using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed record PublicUrlOptions(string PublicBaseUrl)
{
    public static PublicUrlOptions DevelopmentDefault { get; } = new("http://localhost:5000");
}

public sealed class PublicUrlFakeEmailProviderAdapter : IEmailProviderAdapter
{
    private readonly FakeEmailProviderAdapter _inner;
    private readonly PublicUrlOptions _options;

    public PublicUrlFakeEmailProviderAdapter(IFakeMailer fakeMailer, PublicUrlOptions options)
    {
        _inner = new FakeEmailProviderAdapter(fakeMailer);
        _options = options;
    }

    public string ProviderName => _inner.ProviderName;

    public Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var unsubscribeUrl = ToAbsoluteUrl(message.UnsubscribeUrl);
        var plainTextBody = ReplaceRelativeUrl(message.PlainTextBody, message.UnsubscribeUrl, unsubscribeUrl);
        plainTextBody = KeepSingleVisibleUnsubscribeLink(plainTextBody, unsubscribeUrl);
        var metadata = new Dictionary<string, string>(message.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["listUnsubscribe"] = $"<{unsubscribeUrl}>",
            ["listUnsubscribePost"] = "List-Unsubscribe=One-Click"
        };

        var normalized = message with
        {
            PlainTextBody = plainTextBody,
            UnsubscribeUrl = unsubscribeUrl,
            Metadata = metadata
        };

        return _inner.SendAsync(normalized, cancellationToken);
    }

    public Task<EmailProviderWebhookParseResult> ParseWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
        _inner.ParseWebhookAsync(rawBody, headers, cancellationToken);

    public Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
        _inner.ParseInboundWebhookAsync(rawBody, headers, cancellationToken);

    public Task<EmailProviderSendResult> ForwardReplyToClientAsync(ReplyEvent replyEvent, CancellationToken cancellationToken) =>
        _inner.ForwardReplyToClientAsync(replyEvent, cancellationToken);

    private string ToAbsoluteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        var baseUrl = _options.PublicBaseUrl.Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(url) ? "/" : url.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return baseUrl + path;
    }

    private static string ReplaceRelativeUrl(string body, string relativeUrl, string absoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl) || relativeUrl == absoluteUrl)
        {
            return body;
        }

        return body.Replace(relativeUrl, absoluteUrl, StringComparison.Ordinal);
    }

    private static string KeepSingleVisibleUnsubscribeLink(string body, string unsubscribeUrl)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            return body;
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var result = new List<string>(lines.Length);
        var kept = false;

        foreach (var line in lines)
        {
            if (line.Contains(unsubscribeUrl, StringComparison.OrdinalIgnoreCase))
            {
                if (kept)
                {
                    continue;
                }

                kept = true;
            }

            result.Add(line);
        }

        return string.Join(Environment.NewLine, result).TrimEnd();
    }
}

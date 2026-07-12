using System.Net;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Domain.Mailings;

public static class LegacyClickTrackingLinkRestorer
{
    private const string ClickPathPrefix = "/t/click/";

    private static readonly Regex HrefRegex = new(
        "href\\s*=\\s*(?<quote>[\"'])(?<url>.*?)\\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex PlainTrackingUrlRegex = new(
        "(?<url>(?:https?://[^\\s<>\"']+)?/t/click/(?<token>[0-9a-f]{64})(?:[?#][^\\s<>\"']*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string RestoreHtml(string? html, Func<string, string?> originalUrlResolver)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html ?? string.Empty;
        }

        ArgumentNullException.ThrowIfNull(originalUrlResolver);

        return HrefRegex.Replace(html, match =>
        {
            var rawUrl = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!TryExtractToken(rawUrl, out var token))
            {
                return match.Value;
            }

            var originalUrl = ResolveAbsoluteHttpUrl(token, originalUrlResolver);
            if (originalUrl is null)
            {
                return match.Value;
            }

            var quote = match.Groups["quote"].Value;
            return $"href={quote}{WebUtility.HtmlEncode(originalUrl)}{quote}";
        });
    }

    public static string RestoreText(string? text, Func<string, string?> originalUrlResolver)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        ArgumentNullException.ThrowIfNull(originalUrlResolver);

        return PlainTrackingUrlRegex.Replace(text, match =>
        {
            var rawUrl = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!TryExtractToken(rawUrl, out var token))
            {
                return match.Value;
            }

            return ResolveAbsoluteHttpUrl(token, originalUrlResolver) ?? match.Value;
        });
    }

    private static string? ResolveAbsoluteHttpUrl(string token, Func<string, string?> originalUrlResolver)
    {
        var originalUrl = originalUrlResolver(token)?.Trim();
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return originalUrl;
    }

    private static bool TryExtractToken(string? rawUrl, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        var value = rawUrl.Trim();
        string path;
        if (value.StartsWith(ClickPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var end = value.IndexOfAny(['?', '#']);
            path = end < 0 ? value : value[..end];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                 && IsPismoletHost(uri.Host))
        {
            path = uri.AbsolutePath;
        }
        else
        {
            return false;
        }

        if (!path.StartsWith(ClickPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = path[ClickPathPrefix.Length..].Trim('/');
        if (candidate.Length != 64 || candidate.Any(value => !Uri.IsHexDigit(value)))
        {
            return false;
        }

        token = candidate.ToLowerInvariant();
        return true;
    }

    private static bool IsPismoletHost(string host) =>
        host.Equals("pismolet.ru", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".pismolet.ru", StringComparison.OrdinalIgnoreCase)
        || host.Equals("pismolet.test", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".pismolet.test", StringComparison.OrdinalIgnoreCase)
        || host.Equals("pismolet.local", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".pismolet.local", StringComparison.OrdinalIgnoreCase);
}

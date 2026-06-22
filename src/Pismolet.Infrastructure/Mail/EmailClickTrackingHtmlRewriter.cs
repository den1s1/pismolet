using System.Net;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Infrastructure.Mail;

public static class EmailClickTrackingHtmlRewriter
{
    private static readonly Regex EncodedAbsoluteHttpUrlRegex = new(
        "https?://[^\\s<>\"']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string RewriteHtmlEncodedPlainTextLinks(string html, string unsubscribeUrl, Func<string, string?> trackingUrlFactory)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        return EncodedAbsoluteHttpUrlRegex.Replace(html, match => RewriteMatch(match.Value, unsubscribeUrl, trackingUrlFactory));
    }

    private static string RewriteMatch(string encodedMatch, string unsubscribeUrl, Func<string, string?> trackingUrlFactory)
    {
        var (encodedUrl, encodedTrailing) = SplitTrailingPunctuation(encodedMatch);
        if (string.IsNullOrWhiteSpace(encodedUrl))
        {
            return encodedMatch;
        }

        var originalUrl = WebUtility.HtmlDecode(encodedUrl);
        if (!ShouldTrack(originalUrl, unsubscribeUrl))
        {
            return encodedMatch;
        }

        var trackingUrl = trackingUrlFactory(originalUrl);
        if (string.IsNullOrWhiteSpace(trackingUrl))
        {
            return encodedMatch;
        }

        return $"<a href=\"{WebUtility.HtmlEncode(trackingUrl)}\">{encodedUrl}</a>{encodedTrailing}";
    }

    private static bool ShouldTrack(string originalUrl, string unsubscribeUrl)
    {
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(unsubscribeUrl)
            && Uri.TryCreate(unsubscribeUrl.Trim(), UriKind.Absolute, out var unsubscribe)
            && Uri.Compare(uri, unsubscribe, UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.StartsWith("/unsubscribe", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/t/open", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/t/click", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static (string Url, string Trailing) SplitTrailingPunctuation(string value)
    {
        var end = value.Length;
        while (end > 0 && IsTrailingPunctuation(value[end - 1]))
        {
            end--;
        }

        return (value[..end], value[end..]);
    }

    private static bool IsTrailingPunctuation(char value) => value is '.' or ',' or ')' or ']' or '}' or '!' or '?' or ':';
}

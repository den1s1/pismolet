using System.Net;
using Pismolet.Web.Infrastructure.Mail;

namespace Pismolet.Web.Tests;

public sealed class EmailClickTrackingHtmlRewriterTests
{
    [Fact]
    public void Rewrites_absolute_http_links_and_preserves_visible_url()
    {
        var html = WebUtility.HtmlEncode("Сайт https://example.com/page?x=1&y=2.")
            .Replace("\n", "<br>\n", StringComparison.Ordinal);
        var tracked = new List<string>();

        var result = EmailClickTrackingHtmlRewriter.RewriteHtmlEncodedPlainTextLinks(
            html,
            "https://app.pismolet.ru/unsubscribe/token",
            url =>
            {
                tracked.Add(url);
                return "https://app.pismolet.ru/t/click/click-token";
            });

        Assert.Single(tracked);
        Assert.Equal("https://example.com/page?x=1&y=2", tracked[0]);
        Assert.Contains("<a href=\"https://app.pismolet.ru/t/click/click-token\">https://example.com/page?x=1&amp;y=2</a>.", result);
    }

    [Fact]
    public void Does_not_rewrite_unsubscribe_or_service_tracking_links()
    {
        const string unsubscribe = "https://app.pismolet.ru/unsubscribe/token";
        var html = WebUtility.HtmlEncode(string.Join("\n",
            unsubscribe,
            "https://app.pismolet.ru/t/open/open-token.gif",
            "https://app.pismolet.ru/t/click/already-tracked",
            "mailto:info@example.test",
            "tel:+79990000000",
            "/relative/path"))
            .Replace("\n", "<br>\n", StringComparison.Ordinal);
        var tracked = new List<string>();

        var result = EmailClickTrackingHtmlRewriter.RewriteHtmlEncodedPlainTextLinks(
            html,
            unsubscribe,
            url =>
            {
                tracked.Add(url);
                return "https://app.pismolet.ru/t/click/click-token";
            });

        Assert.Empty(tracked);
        Assert.DoesNotContain("href=\"https://app.pismolet.ru/t/click/click-token\"", result);
        Assert.Contains(WebUtility.HtmlEncode(unsubscribe), result);
        Assert.Contains("https://app.pismolet.ru/t/open/open-token.gif", result);
    }
}

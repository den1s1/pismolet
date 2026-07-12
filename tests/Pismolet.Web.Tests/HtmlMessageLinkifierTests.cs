using Pismolet.Web.Domain.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class HtmlMessageLinkifierTests
{
    [Fact]
    public void Linkify_converts_single_https_url()
    {
        var result = HtmlMessageLinkifier.Linkify("<p>https://example.org/news</p>");

        Assert.Equal("<p><a href=\"https://example.org/news\">https://example.org/news</a></p>", result);
    }

    [Fact]
    public void Linkify_converts_single_http_url()
    {
        var result = HtmlMessageLinkifier.Linkify("<div>http://example.org/page</div>");

        Assert.Equal("<div><a href=\"http://example.org/page\">http://example.org/page</a></div>", result);
    }

    [Fact]
    public void Linkify_converts_multiple_urls_independently()
    {
        var result = HtmlMessageLinkifier.Linkify("<p>Первый: https://one.example/a, второй: http://two.example/b.</p>");

        Assert.Equal(
            "<p>Первый: <a href=\"https://one.example/a\">https://one.example/a</a>, второй: <a href=\"http://two.example/b\">http://two.example/b</a>.</p>",
            result);
    }

    [Fact]
    public void Linkify_does_not_wrap_existing_anchor_again()
    {
        const string html = "<p><a href=\"https://example.org/a\">https://example.org/a</a> и https://example.org/b</p>";

        var result = HtmlMessageLinkifier.Linkify(html);

        Assert.Equal(
            "<p><a href=\"https://example.org/a\">https://example.org/a</a> и <a href=\"https://example.org/b\">https://example.org/b</a></p>",
            result);
        Assert.DoesNotContain("<a href=\"https://example.org/a\"><a", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Linkify_does_not_change_urls_inside_attributes()
    {
        const string html = "<div data-url=\"https://example.org/data\" style=\"background-image:url(https://example.org/image.png)\">Текст</div>";

        var result = HtmlMessageLinkifier.Linkify(html);

        Assert.Equal(html, result);
    }

    [Theory]
    [InlineData("https://example.org/page.", "<a href=\"https://example.org/page\">https://example.org/page</a>.")]
    [InlineData("https://example.org/page,", "<a href=\"https://example.org/page\">https://example.org/page</a>,")]
    [InlineData("(https://example.org/page)", "(<a href=\"https://example.org/page\">https://example.org/page</a>)")]
    [InlineData("https://example.org/a(b)", "<a href=\"https://example.org/a(b)\">https://example.org/a(b)</a>")]
    public void Linkify_keeps_trailing_punctuation_outside_link(string source, string expected)
    {
        Assert.Equal(expected, HtmlMessageLinkifier.Linkify(source));
    }

    [Fact]
    public void Linkify_does_not_convert_dangerous_schemes_or_email_parts()
    {
        const string html = "<p>javascript:https://evil.example data:https://evil.example name@https://example.org user@example.org</p>";

        var result = HtmlMessageLinkifier.Linkify(html);

        Assert.Equal(html, result);
        Assert.DoesNotContain("<a ", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Linkify_does_not_process_service_footer()
    {
        const string html = "<p>https://example.org/user</p><div class=\"pismolet-footer\">https://example.org/service</div>";

        var result = HtmlMessageLinkifier.Linkify(html);

        Assert.Equal(
            "<p><a href=\"https://example.org/user\">https://example.org/user</a></p><div class=\"pismolet-footer\">https://example.org/service</div>",
            result);
    }

    [Theory]
    [InlineData("https://app.pismolet.ru/unsubscribe/token")]
    [InlineData("https://app.pismolet.ru/t/open/token.gif")]
    [InlineData("https://app.pismolet.ru/t/click/token")]
    public void Linkify_does_not_rewrite_service_tracking_urls(string url)
    {
        Assert.Equal($"<p>{url}</p>", HtmlMessageLinkifier.Linkify($"<p>{url}</p>"));
    }

    [Theory]
    [InlineData("https://example.org/unsubscribe/article")]
    [InlineData("https://example.org/t/open/article")]
    [InlineData("https://example.org/t/click/article")]
    public void Linkify_keeps_external_urls_with_similar_paths_as_user_links(string url)
    {
        Assert.Equal($"<p><a href=\"{url}\">{url}</a></p>", HtmlMessageLinkifier.Linkify($"<p>{url}</p>"));
    }

    [Fact]
    public void Linkify_is_idempotent()
    {
        const string source = "<p>Сайт: https://example.org/path</p>";

        var once = HtmlMessageLinkifier.Linkify(source);
        var twice = HtmlMessageLinkifier.Linkify(once);

        Assert.Equal(once, twice);
        Assert.Equal(1, CountOccurrences(twice, "<a href="));
    }

    [Fact]
    public void Linkify_keeps_encoded_query_readable_and_safe()
    {
        var result = HtmlMessageLinkifier.Linkify("<p>https://example.org/search?a=1&amp;b=2</p>");

        Assert.Equal(
            "<p><a href=\"https://example.org/search?a=1&amp;b=2\">https://example.org/search?a=1&amp;b=2</a></p>",
            result);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}

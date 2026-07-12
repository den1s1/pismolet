using Pismolet.Web.Domain.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class TrackedLinkSafetyTests
{
    [Theory]
    [InlineData("https://app.pismolet.ru/unsubscribe/token")]
    [InlineData("https://app.pismolet.ru/t/open/token.gif")]
    [InlineData("https://app.pismolet.ru/t/click/token")]
    public void Create_rejects_technical_service_links(string url)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TrackedLink.Create(Guid.NewGuid(), "reader@example.test", url));

        Assert.Contains("Служебные ссылки", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.org/unsubscribe/article")]
    [InlineData("https://example.org/t/open/article")]
    [InlineData("https://example.org/t/click/article")]
    public void Create_keeps_external_links_with_similar_paths_trackable(string url)
    {
        var link = TrackedLink.Create(Guid.NewGuid(), "reader@example.test", url);

        Assert.Equal(url, link.OriginalUrl);
        Assert.False(string.IsNullOrWhiteSpace(link.Token));
    }

    [Fact]
    public void Create_keeps_regular_http_link_compatible()
    {
        var mailingId = Guid.NewGuid();

        var link = TrackedLink.Create(mailingId, "Reader@Example.Test", " https://example.org/news ");

        Assert.Equal(mailingId, link.MailingId);
        Assert.Equal("reader@example.test", link.RecipientEmail);
        Assert.Equal("https://example.org/news", link.OriginalUrl);
        Assert.False(string.IsNullOrWhiteSpace(link.Token));
    }
}

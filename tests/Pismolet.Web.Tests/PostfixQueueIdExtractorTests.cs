using Pismolet.Web.Infrastructure.Mail;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class PostfixQueueIdExtractorTests
{
    [Fact]
    public void TryExtract_returns_queue_id_from_Postfix_SMTP_response()
    {
        var actual = PostfixQueueIdExtractor.TryExtract("2.0.0 Ok: queued as 3CB4CF853C");

        Assert.Equal("3CB4CF853C", actual);
    }

    [Fact]
    public void TryExtract_normalizes_queue_id_to_uppercase()
    {
        var actual = PostfixQueueIdExtractor.TryExtract("250 2.0.0 Ok: queued as abc123def");

        Assert.Equal("ABC123DEF", actual);
    }

    [Fact]
    public void TryExtract_returns_null_when_response_does_not_contain_queue_id()
    {
        var actual = PostfixQueueIdExtractor.TryExtract("2.0.0 Ok");

        Assert.Null(actual);
    }
}

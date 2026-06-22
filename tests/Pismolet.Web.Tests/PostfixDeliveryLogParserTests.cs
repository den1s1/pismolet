using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class PostfixDeliveryLogParserTests
{
    [Fact]
    public void Postfix_parser_parses_sent_delivery_line()
    {
        const string line = "Jun 22 13:44:23 mail postfix/smtp[12345]: ABCDEF1234: to=<User@Example.com>, relay=mx.example.com[192.0.2.1]:25, delay=1.2, delays=0.01/0.01/0.5/0.68, dsn=2.0.0, status=sent (250 2.0.0 Ok: queued as 12345)";

        var parsed = PostfixDeliveryLogParser.TryParse(line, 2026, TimeSpan.Zero, out var deliveryEvent);

        Assert.True(parsed);
        Assert.NotNull(deliveryEvent);
        Assert.Equal("ABCDEF1234", deliveryEvent.QueueId);
        Assert.Equal("user@example.com", deliveryEvent.RecipientEmail);
        Assert.Equal(PostfixDeliveryLogStatus.Sent, deliveryEvent.Status);
        Assert.Equal(DeliveryStatus.Delivered, deliveryEvent.DeliveryStatus);
        Assert.Equal("2.0.0", deliveryEvent.Dsn);
        Assert.Equal("mx.example.com[192.0.2.1]:25", deliveryEvent.Relay);
        Assert.Equal("250 2.0.0 Ok: queued as 12345", deliveryEvent.Diagnostic);
        Assert.Equal(DateTimeOffset.Parse("2026-06-22T13:44:23+00:00"), deliveryEvent.OccurredAt);
    }

    [Fact]
    public void Postfix_parser_parses_iso_timestamp_delivery_line()
    {
        const string line = "2026-06-22T17:01:14.999216+00:00 mail postfix/smtp[54675]: BCC9083ED5: to=<den1s@mail.ru>, relay=mxs.mail.ru[94.100.180.31]:25, delay=1.2, delays=0.11/0.01/0.02/1.1, dsn=2.0.0, status=sent (250 OK id=1wbi1J-00000000BmK-3yPw)";

        var parsed = PostfixDeliveryLogParser.TryParse(line, 2026, TimeSpan.Zero, out var deliveryEvent);

        Assert.True(parsed);
        Assert.NotNull(deliveryEvent);
        Assert.Equal("BCC9083ED5", deliveryEvent.QueueId);
        Assert.Equal("den1s@mail.ru", deliveryEvent.RecipientEmail);
        Assert.Equal(PostfixDeliveryLogStatus.Sent, deliveryEvent.Status);
        Assert.Equal(DeliveryStatus.Delivered, deliveryEvent.DeliveryStatus);
        Assert.Equal("2.0.0", deliveryEvent.Dsn);
        Assert.Equal("mxs.mail.ru[94.100.180.31]:25", deliveryEvent.Relay);
        Assert.Equal("250 OK id=1wbi1J-00000000BmK-3yPw", deliveryEvent.Diagnostic);
        Assert.Equal(DateTimeOffset.Parse("2026-06-22T17:01:14.999216+00:00"), deliveryEvent.OccurredAt);
    }

    [Fact]
    public void Postfix_parser_parses_deferred_delivery_line()
    {
        const string line = "Jun 22 13:45:23 mail postfix/smtp[12345]: 0A1B2C3D4E: to=<user@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=3.1, delays=0.01/0.01/1.5/1.58, dsn=4.4.1, status=deferred (connect to mx.example.com[192.0.2.1]:25: Connection timed out)";

        var parsed = PostfixDeliveryLogParser.TryParse(line, 2026, TimeSpan.Zero, out var deliveryEvent);

        Assert.True(parsed);
        Assert.NotNull(deliveryEvent);
        Assert.Equal(PostfixDeliveryLogStatus.Deferred, deliveryEvent.Status);
        Assert.Equal(DeliveryStatus.SoftBounce, deliveryEvent.DeliveryStatus);
        Assert.Equal("4.4.1", deliveryEvent.Dsn);
        Assert.Contains("Connection timed out", deliveryEvent.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Postfix_parser_parses_bounced_delivery_line()
    {
        const string line = "Jun 22 13:46:23 mail postfix/smtp[12345]: 123ABC456D: to=<bad@example.com>, relay=mx.example.com[192.0.2.1]:25, delay=0.6, delays=0.01/0.01/0.3/0.28, dsn=5.1.1, status=bounced (host mx.example.com said: 550 5.1.1 User unknown)";

        var parsed = PostfixDeliveryLogParser.TryParse(line, 2026, TimeSpan.Zero, out var deliveryEvent);

        Assert.True(parsed);
        Assert.NotNull(deliveryEvent);
        Assert.Equal(PostfixDeliveryLogStatus.Bounced, deliveryEvent.Status);
        Assert.Equal(DeliveryStatus.HardBounce, deliveryEvent.DeliveryStatus);
        Assert.Equal("5.1.1", deliveryEvent.Dsn);
        Assert.Contains("User unknown", deliveryEvent.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Postfix_parser_ignores_non_smtp_lines()
    {
        const string line = "Jun 22 13:44:23 mail postfix/qmgr[12345]: ABCDEF1234: removed";

        var parsed = PostfixDeliveryLogParser.TryParse(line, 2026, TimeSpan.Zero, out var deliveryEvent);

        Assert.False(parsed);
        Assert.Null(deliveryEvent);
    }
}

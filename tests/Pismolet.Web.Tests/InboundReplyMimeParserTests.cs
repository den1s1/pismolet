using System.Text;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class InboundReplyMimeParserTests
{
    [Fact]
    public async Task Parser_reads_plain_reply_and_token_from_envelope()
    {
        var parser = new PostfixRawMimeInboundReplyParser();
        var token = "v1.payload.signature";
        var raw = BuildPlainMessage("recipient@example.test", "Ответ", "Здравствуйте!");

        var result = await parser.ParseAsync(new InboundReplyRawMessage(
            Encoding.UTF8.GetBytes(raw),
            EnvelopeRecipient: $"reply+{token}{Convert.ToChar(64)}reply.pismolet.test",
            SourceId: "queue-1"), CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Event);
        Assert.Equal("PostfixSpool", result.Event.Provider);
        Assert.Equal(token, result.Event.ReplyToken);
        Assert.Equal("client@example.test", result.Event.FromEmail);
        Assert.Equal("Ответ", result.Event.Subject);
        Assert.Contains("Здравствуйте", result.Event.TextBody);
    }

    [Fact]
    public async Task Parser_reads_token_from_original_to_header()
    {
        var parser = new PostfixRawMimeInboundReplyParser();
        var token = "v1.payload.signature";
        var raw = BuildPlainMessage($"reply+{token}{Convert.ToChar(64)}reply.pismolet.test", "Re", "Reply body", extraHeaders: $"X-Original-To: reply+{token}{Convert.ToChar(64)}reply.pismolet.test\r\n");

        var result = await parser.ParseAsync(new InboundReplyRawMessage(
            Encoding.UTF8.GetBytes(raw),
            EnvelopeRecipient: null,
            SourceId: "queue-2"), CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Event);
        Assert.Equal(token, result.Event.ReplyToken);
    }

    [Fact]
    public async Task Parser_supports_token_as_local_part()
    {
        var parser = new PostfixRawMimeInboundReplyParser();
        var token = "v1.payload.signature";
        var raw = BuildPlainMessage($"{token}{Convert.ToChar(64)}reply.pismolet.test", "Re", "Reply body");

        var result = await parser.ParseAsync(new InboundReplyRawMessage(
            Encoding.UTF8.GetBytes(raw),
            EnvelopeRecipient: null,
            SourceId: "queue-3"), CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Event);
        Assert.Equal(token, result.Event.ReplyToken);
    }

    private static string BuildPlainMessage(string to, string subject, string body, string extraHeaders = "") => string.Join("\r\n",
        "From: Client <client@example.test>",
        $"To: {to}",
        $"Subject: {subject}",
        "Message-Id: <reply-1@example.test>",
        "Date: Sat, 27 Jun 2026 22:20:00 +0000",
        "MIME-Version: 1.0",
        extraHeaders + "Content-Type: text/plain; charset=utf-8",
        "",
        body);
}

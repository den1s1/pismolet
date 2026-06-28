using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class ReplyAliasSendingTests
{
    [Fact]
    public async Task Fake_provider_keeps_reply_alias_and_message_id()
    {
        var fakeMailer = new InMemoryFakeMailer();
        var adapter = new FakeEmailProviderAdapter(fakeMailer);
        var messageId = "pismolet-test@reply.pismolet.ru";
        var message = new EmailMessage(
            Guid.NewGuid(),
            new EmailRecipient("recipient@example.ru"),
            "Письмолёт",
            "Тест",
            "Тело письма",
            "/unsubscribe/token",
            "PISMOLET-TEST",
            "user@reply.pismolet.ru",
            string.Empty,
            new Dictionary<string, string>
            {
                ["replyAlias"] = "user",
                ["outboundMessageId"] = messageId
            },
            BodyFormat: MessageBodyFormat.Text,
            MessageId: messageId);

        var result = await adapter.SendAsync(message, CancellationToken.None);

        Assert.True(result.Accepted);
        var sent = Assert.Single(fakeMailer.GetOutbox());
        Assert.Equal("user@reply.pismolet.ru", sent.ReplyToAddress);
        Assert.Equal(messageId, sent.MessageId);
        Assert.True(string.IsNullOrWhiteSpace(sent.ReplyToken));
    }
}

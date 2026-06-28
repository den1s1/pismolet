using System.Collections.Concurrent;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Domain.Mail;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class InMemoryFakeMailer : IFakeMailer
{
    private readonly ConcurrentQueue<FakeMail> _outbox = new();

    public void SendConfirmation(string to, string subject, string link) => _outbox.Enqueue(new FakeMail(
        To: to,
        Subject: subject,
        Link: link,
        CreatedAt: DateTimeOffset.UtcNow));

    public void SendAdminNotification(string to, string subject, string body, string? link = null) => _outbox.Enqueue(new FakeMail(
        To: to,
        Subject: subject,
        Link: link ?? string.Empty,
        CreatedAt: DateTimeOffset.UtcNow,
        TextBody: body));

    public void AddMailingMessage(
        string to,
        string subject,
        string link,
        string? replyToAddress = null,
        string? replyToken = null,
        string? providerMessageId = null,
        string? textBody = null) => _outbox.Enqueue(new FakeMail(
            To: to,
            Subject: subject,
            Link: link,
            CreatedAt: DateTimeOffset.UtcNow,
            ReplyToAddress: replyToAddress,
            ReplyToken: replyToken,
            ProviderMessageId: providerMessageId,
            TextBody: textBody));

    public void AddForwardedReply(string to, string subject, string fromEmail, string textBody, string providerMessageId) => _outbox.Enqueue(new FakeMail(
        To: to,
        Subject: subject,
        Link: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow,
        ProviderMessageId: providerMessageId,
        TextBody: textBody,
        FromEmail: fromEmail,
        IsForwardedReply: true));

    public IReadOnlyCollection<FakeMail> GetOutbox() => _outbox.Reverse().ToArray();
}

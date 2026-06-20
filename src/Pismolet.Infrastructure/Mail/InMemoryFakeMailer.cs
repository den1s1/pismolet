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

    public void AddMailingMessage(string to, string subject, string link) => _outbox.Enqueue(new FakeMail(
        To: to,
        Subject: subject,
        Link: link,
        CreatedAt: DateTimeOffset.UtcNow));

    public IReadOnlyCollection<FakeMail> GetOutbox() => _outbox.Reverse().ToArray();
}

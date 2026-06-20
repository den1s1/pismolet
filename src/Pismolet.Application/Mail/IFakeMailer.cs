using Pismolet.Web.Domain.Mail;

namespace Pismolet.Web.Application.Mail;

public interface IFakeMailer
{
    void SendConfirmation(string to, string subject, string link);

    void AddMailingMessage(string to, string subject, string link);

    IReadOnlyCollection<FakeMail> GetOutbox();
}

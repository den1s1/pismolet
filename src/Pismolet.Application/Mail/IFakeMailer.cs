using Pismolet.Web.Domain.Mail;

namespace Pismolet.Web.Application.Mail;

public interface IFakeMailer
{
    void SendConfirmation(string to, string subject, string link);

    void AddMailingMessage(
        string to,
        string subject,
        string link,
        string? replyToAddress = null,
        string? replyToken = null,
        string? providerMessageId = null,
        string? textBody = null);

    void AddForwardedReply(string to, string subject, string fromEmail, string textBody, string providerMessageId);

    IReadOnlyCollection<FakeMail> GetOutbox();
}

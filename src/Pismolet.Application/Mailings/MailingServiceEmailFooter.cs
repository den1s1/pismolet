namespace Pismolet.Web.Application.Mailings;

public static class MailingServiceEmailFooter
{
    public const string ReasonPrefix = "Вы получили это письмо от ";

    public static string Reason(string senderName)
    {
        var sender = string.IsNullOrWhiteSpace(senderName) ? "отправителя" : senderName.Trim();
        return $"{ReasonPrefix}{sender} через Письмолёт, потому что отправитель указал, что у него есть законное основание связаться с вами по этому адресу.";
    }

    public static string UnsubscribeLine(string unsubscribeUrl) =>
        $"Отписаться от писем через Письмолёт:\n{unsubscribeUrl}";

    public static string ServiceIdentifier(string publicId) =>
        $"Служебный идентификатор рассылки: {publicId}";

    public static string PlainText(string body, string senderName, string unsubscribeUrl, string serviceIdentifier) =>
        string.Join("\n\n", body.TrimEnd(), Reason(senderName), UnsubscribeLine(unsubscribeUrl));
}

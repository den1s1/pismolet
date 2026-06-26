namespace Pismolet.Web.Application.Mailings;

public static class MailingServiceEmailFooter
{
    public static string Reason(string senderName)
    {
        var sender = string.IsNullOrWhiteSpace(senderName) ? "отправителя" : senderName.Trim();
        return $"Вы получили это письмо от {sender} через Письмолёт, потому что отправитель указал, что у него есть законное основание связаться с вами по этому адресу. Если вы не хотите получать такие письма через Письмолёт, вы можете отписаться от всех рассылок через сервис.";
    }

    public static string UnsubscribeLine(string unsubscribeUrl) =>
        $"Отписаться от всех рассылок через сервис: {unsubscribeUrl}";

    public static string ServiceIdentifier(string publicId) =>
        $"Служебный идентификатор рассылки: {publicId}";

    public static string PlainText(string body, string senderName, string unsubscribeUrl, string serviceIdentifier) =>
        string.Join("\n\n", body, Reason(senderName), UnsubscribeLine(unsubscribeUrl), serviceIdentifier);
}

namespace Pismolet.Web.Application.Mailings;

public static class MailingServiceEmailFooter
{
    public const string ReasonPrefix = "Вы получили это письмо от ";

    public const string UnsubscribeExplanation = "Если вы не хотите получать письма через Письмолёт, вы можете отписаться от всех рассылок через сервис.";

    public static string Reason(string recipientReason)
    {
        var reason = recipientReason?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(reason)
            ? UnsubscribeExplanation
            : $"{reason} {UnsubscribeExplanation}";
    }

    public static string LegacyRecipientReason(string senderName)
    {
        var sender = string.IsNullOrWhiteSpace(senderName) ? "отправителя" : senderName.Trim();
        return $"{ReasonPrefix}{sender} через Письмолёт, потому что отправитель указал, что у него есть законное основание связаться с вами по этому адресу.";
    }

    public static string UnsubscribeLine(string unsubscribeUrl) =>
        $"Отписаться от всех рассылок через сервис: {unsubscribeUrl}";

    public static string ServiceIdentifier(string publicId) =>
        $"Служебный идентификатор рассылки: {publicId}";

    public static string PlainText(string body, string recipientReason, string unsubscribeUrl, string serviceIdentifier) =>
        string.Join("\n\n", body, Reason(recipientReason), UnsubscribeLine(unsubscribeUrl), serviceIdentifier);
}

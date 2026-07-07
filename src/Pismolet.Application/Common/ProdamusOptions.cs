namespace Pismolet.Web.Application.Common;

public sealed record ProdamusOptions(
    string PaymentPageUrl,
    string CallbackCheckValue,
    bool CallbackCheckRequired,
    string ServiceName,
    bool IsTest)
{
    public const string DefaultPaymentPageUrl = "";
    public const string DefaultServiceName = "Техническая подготовка и отправка email-рассылки по базе клиента";

    public bool HasPaymentPageUrl => !string.IsNullOrWhiteSpace(PaymentPageUrl);
    public bool HasCallbackCheckValue => !string.IsNullOrWhiteSpace(CallbackCheckValue);
}

namespace Pismolet.Web.Application.Common;

public sealed record ProdamusOptions(
    string PaymentPageUrl,
    string CallbackCheckValue,
    bool CallbackCheckRequired,
    string ServiceName,
    bool IsTest)
{
    public const string LocalFakePaymentPageUrl = "/payments/prodamus/fake/checkout";
    public const string DefaultServiceName = "Техническая подготовка и отправка email-рассылки по базе клиента";

    public bool HasCallbackCheckValue => !string.IsNullOrWhiteSpace(CallbackCheckValue);
}

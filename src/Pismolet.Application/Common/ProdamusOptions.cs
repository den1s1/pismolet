namespace Pismolet.Web.Application.Common;

public sealed record ProdamusOptions(
    string PaymentPageUrl,
    string PaymentPageSignatureKey,
    string SysCode,
    string CallbackCheckValue,
    bool CallbackCheckRequired,
    string ServiceName,
    bool IsTest)
{
    public const string DefaultPaymentPageUrl = "https://pismolet.payform.ru/";
    public const string DefaultServiceName = "Техническая подготовка и отправка email-рассылки по базе клиента";

    public bool HasPaymentPageUrl => !string.IsNullOrWhiteSpace(PaymentPageUrl);
    public bool HasPaymentPageSignatureKey => !string.IsNullOrWhiteSpace(PaymentPageSignatureKey);
    public bool HasCallbackCheckValue => !string.IsNullOrWhiteSpace(CallbackCheckValue);
    public bool HasSysCode => !string.IsNullOrWhiteSpace(SysCode);
}

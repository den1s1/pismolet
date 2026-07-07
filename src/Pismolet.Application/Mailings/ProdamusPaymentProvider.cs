using Pismolet.Web.Application.Common;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed class ProdamusPaymentProvider : IPaymentProvider
{
    public PaymentAttempt Start(Payment payment)
    {
        var operationId = ProdamusPaymentForm.BuildOrderId(payment.Id);
        return payment.Attempts.FirstOrDefault(x => x.ProviderOperationId == operationId)
            ?? PaymentAttempt.Pending(payment.Id, operationId, ProdamusPaymentForm.ProviderName);
    }

    public PaymentAttempt ConfirmSuccess(Payment payment, string providerOperationId, string rawCallback = "success")
    {
        var attempt = payment.Attempts.FirstOrDefault(x => x.ProviderOperationId == providerOperationId)
            ?? PaymentAttempt.Pending(payment.Id, providerOperationId, ProdamusPaymentForm.ProviderName);
        return attempt.Status == PaymentAttemptStatus.Succeeded ? attempt : attempt.MarkSucceeded(rawCallback);
    }
}

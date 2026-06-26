using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed class AdminWaivedMailingPaymentService(
    MailingPaymentService inner,
    IMailingRepository mailings,
    IPaymentRepository payments,
    IPriceSettingsRepository prices,
    IUserRepository users,
    IEmailNormalizer emailNormalizer,
    IAuditLogger auditLogger,
    IAdminAccessService adminAccess) : IMailingPaymentService
{
    public MailingPaymentResult GetPaymentReview(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var result = inner.GetPaymentReview(userEmail, mailingId, request);
        return IsAdminReview(result) ? MakeZeroAmount(result) : result;
    }

    public MailingPaymentResult StartPayment(string userEmail, Guid mailingId, RequestMetadata request)
    {
        if (!adminAccess.IsAdminEmail(userEmail))
        {
            return inner.StartPayment(userEmail, mailingId, request);
        }

        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null)
        {
            return MailingPaymentResult.Failure("Рассылка не найдена.");
        }

        var blockError = ValidateNotBlocked(mailing);
        if (!string.IsNullOrWhiteSpace(blockError))
        {
            return MailingPaymentResult.Failure(blockError);
        }

        try
        {
            MailingPricingService.ValidateReadyForPayment(mailing);
            var payment = payments.GetByMailingId(mailingId);
            if (payment is null)
            {
                var settings = prices.GetActive();
                var accepted = mailing.Recipients.Count(x => x.Status == RecipientStatus.Accepted);
                var excluded = mailing.Recipients.Count(x => x.Status != RecipientStatus.Accepted);
                payment = Payment.CreateFree(mailing.Id, mailing.OwnerEmail, accepted, excluded, settings.Currency);
                WriteAudit(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "admin_price_waived", request.Ip, request.UserAgent, $"mailingId={mailing.Id};amount=0;currency={payment.Currency}"));
            }

            if (payment.Status != PaymentStatus.Paid)
            {
                payment = payment with { PricePerRecipient = 0m, TotalAmount = 0m };
                var attempt = PaymentAttempt.Succeeded(payment.Id, $"admin-waived-{mailing.Id:N}", PaymentAttempt.AdminFreeProvider, "admin_waived");
                payment = payment.MarkPaid(attempt);
                payments.Save(payment);
                mailing = mailing.WithStatus(MailingStatus.Paid);
                mailings.Update(mailing);
                WriteAudit(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "admin_payment_waived", request.Ip, request.UserAgent, $"mailingId={mailing.Id};paymentId={payment.Id};attemptId={attempt.Id}"));
            }

            return MailingPaymentResult.Success(BuildZeroAmountReview(mailing.WithStatus(MailingStatus.Paid), payment));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return MailingPaymentResult.Failure(ex.Message);
        }
    }

    public MailingPaymentResult ConfirmPayment(string userEmail, Guid mailingId, string providerOperationId, RequestMetadata request) =>
        inner.ConfirmPayment(userEmail, mailingId, providerOperationId, request);

    public MailingPaymentResult ConfirmProviderPayment(string providerOperationId, RequestMetadata request, string rawCallback) =>
        inner.ConfirmProviderPayment(providerOperationId, request, rawCallback);

    private bool IsAdminReview(MailingPaymentResult result) => result.Review is not null && adminAccess.IsAdminEmail(result.Review.Mailing.OwnerEmail);

    private static MailingPaymentResult MakeZeroAmount(MailingPaymentResult result) => result.Review is null
        ? result
        : MailingPaymentResult.Success(BuildZeroAmountReview(result.Review.Mailing, result.Review.Payment));

    private static MailingPaymentReview BuildZeroAmountReview(Mailing mailing, Payment? payment)
    {
        var accepted = mailing.Recipients.Count(x => x.Status == RecipientStatus.Accepted);
        var duplicates = mailing.Recipients.Count(x => x.Status == RecipientStatus.Duplicate);
        var invalid = mailing.Recipients.Count(x => x.Status == RecipientStatus.Invalid);
        var suppressed = mailing.Recipients.Count(x => x.Status == RecipientStatus.GloballySuppressed);
        var excluded = duplicates + invalid + suppressed;
        return new MailingPaymentReview(mailing, accepted, excluded, duplicates, invalid, suppressed, 0m, 0m, payment?.Currency ?? MailingTariff.Currency, payment);
    }

    private string ValidateNotBlocked(Mailing mailing)
    {
        if (mailing.Status == MailingStatus.Blocked)
        {
            return "Рассылка заблокирована администратором.";
        }

        return users.GetByEmail(mailing.OwnerEmail)?.Profile.IsBlocked == true
            ? "Клиент заблокирован администратором."
            : string.Empty;
    }

    private Mailing? GetOwnedMailing(string userEmail, Guid mailingId)
    {
        var normalized = emailNormalizer.Normalize(userEmail);
        return string.IsNullOrWhiteSpace(normalized) ? null : mailings.GetForOwner(mailingId, normalized);
    }

    private void WriteAudit(AuditRecord record)
    {
        try
        {
            auditLogger.Write(record);
        }
        catch
        {
            // Audit write must not break the admin free launch path.
        }
    }
}

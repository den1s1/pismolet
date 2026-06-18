using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record MailingPaymentReview(Mailing Mailing, int AcceptedRecipientsCount, int ExcludedRecipientsCount, int DuplicateRecipientsCount, int InvalidRecipientsCount, int GloballySuppressedRecipientsCount, decimal PricePerRecipient, decimal TotalAmount, string Currency, Payment? Payment);

public sealed record MailingPaymentResult(bool Ok, string Error, MailingPaymentReview? Review)
{
    public static MailingPaymentResult Success(MailingPaymentReview review) => new(true, string.Empty, review);
    public static MailingPaymentResult Failure(string error) => new(false, error, null);
}

public interface IMailingPricingService
{
    MailingPaymentReview Calculate(Mailing mailing, Payment? payment = null);
}

public interface IPaymentProvider
{
    PaymentAttempt Start(Payment payment);
    PaymentAttempt ConfirmSuccess(Payment payment, string providerOperationId);
}

public interface IMailingPaymentService
{
    MailingPaymentResult GetPaymentReview(string userEmail, Guid mailingId, RequestMetadata request);
    MailingPaymentResult StartPayment(string userEmail, Guid mailingId, RequestMetadata request);
    MailingPaymentResult ConfirmPayment(string userEmail, Guid mailingId, string providerOperationId, RequestMetadata request);
}

public sealed class MailingPricingService(IPriceSettingsRepository prices) : IMailingPricingService
{
    public MailingPaymentReview Calculate(Mailing mailing, Payment? payment = null)
    {
        ValidateReadyForPayment(mailing);
        var settings = prices.GetActive();
        settings.EnsureValid();
        var accepted = mailing.Recipients.Count(x => x.Status == RecipientStatus.Accepted);
        var duplicates = mailing.Recipients.Count(x => x.Status == RecipientStatus.Duplicate);
        var invalid = mailing.Recipients.Count(x => x.Status == RecipientStatus.Invalid);
        var suppressed = mailing.Recipients.Count(x => x.Status == RecipientStatus.GloballySuppressed);
        var excluded = duplicates + invalid + suppressed;
        return new MailingPaymentReview(mailing, accepted, excluded, duplicates, invalid, suppressed, settings.PricePerRecipient, accepted * settings.PricePerRecipient, settings.Currency, payment);
    }

    public static void ValidateReadyForPayment(Mailing mailing)
    {
        if (mailing.LastImportStats.Accepted <= 0 || mailing.Recipients.All(x => x.Status != RecipientStatus.Accepted)) throw new InvalidOperationException("Сначала загрузите адреса для рассылки.");
        if (mailing.Declaration is null || !mailing.Declaration.IsBaseLegalityConfirmed) throw new InvalidOperationException("Сначала подтвердите базу адресов.");
        if (mailing.MessageDraft is null) throw new InvalidOperationException("Сначала сохраните текст письма.");
    }
}

public sealed class FakePaymentProvider : IPaymentProvider
{
    public PaymentAttempt Start(Payment payment)
    {
        var operationId = $"fake-{payment.Id:N}";
        return payment.Attempts.FirstOrDefault(x => x.ProviderOperationId == operationId) ?? PaymentAttempt.Pending(payment.Id, operationId);
    }

    public PaymentAttempt ConfirmSuccess(Payment payment, string providerOperationId)
    {
        var attempt = payment.Attempts.FirstOrDefault(x => x.ProviderOperationId == providerOperationId) ?? PaymentAttempt.Pending(payment.Id, providerOperationId);
        return attempt.Status == PaymentAttemptStatus.Succeeded ? attempt : attempt.MarkSucceeded("success");
    }
}

public sealed class MailingPaymentService(IMailingRepository mailings, IPaymentRepository payments, IPriceSettingsRepository prices, IMailingPricingService pricing, IPaymentProvider provider, IEmailNormalizer emailNormalizer, IAuditLogger auditLogger) : IMailingPaymentService
{
    public MailingPaymentResult GetPaymentReview(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        return mailing is null ? MailingPaymentResult.Failure("Рассылка не найдена.") : BuildReview(mailing, payments.GetByMailingId(mailingId));
    }

    public MailingPaymentResult StartPayment(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null) return MailingPaymentResult.Failure("Рассылка не найдена.");

        try
        {
            MailingPricingService.ValidateReadyForPayment(mailing);
            var payment = payments.GetByMailingId(mailingId);
            if (payment is null)
            {
                var settings = prices.GetActive();
                var accepted = mailing.Recipients.Count(x => x.Status == RecipientStatus.Accepted);
                var excluded = mailing.Recipients.Count(x => x.Status != RecipientStatus.Accepted);
                payment = Payment.Create(mailing.Id, mailing.OwnerEmail, accepted, excluded, settings);
                auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_price_calculated", request.Ip, request.UserAgent, $"mailingId={mailing.Id};amount={payment.TotalAmount};currency={payment.Currency}"));
            }

            if (payment.Status != PaymentStatus.Paid)
            {
                var attempt = provider.Start(payment);
                payment = payment.WithAttempt(attempt);
                payments.Save(payment);
                mailing = mailing.WithStatus(MailingStatus.PaymentPending);
                mailings.Update(mailing);
                auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "payment_attempt_created", request.Ip, request.UserAgent, $"mailingId={mailing.Id};paymentId={payment.Id};attemptId={attempt.Id}"));
            }

            var status = payment.Status == PaymentStatus.Paid ? MailingStatus.Paid : MailingStatus.PaymentPending;
            return BuildReview(mailing.WithStatus(status), payment);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return MailingPaymentResult.Failure(ex.Message);
        }
    }

    public MailingPaymentResult ConfirmPayment(string userEmail, Guid mailingId, string providerOperationId, RequestMetadata request)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null) return MailingPaymentResult.Failure("Рассылка не найдена.");
        var payment = payments.GetByMailingId(mailingId);
        if (payment is null) return MailingPaymentResult.Failure("Сначала создайте оплату рассылки.");

        if (payment.Status != PaymentStatus.Paid)
        {
            var attempt = provider.ConfirmSuccess(payment, providerOperationId);
            payment = payment.MarkPaid(attempt);
            payments.Save(payment);
            mailing = mailing.WithStatus(MailingStatus.Paid);
            mailings.Update(mailing);
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "fake_payment_succeeded", request.Ip, request.UserAgent, $"mailingId={mailing.Id};paymentId={payment.Id};attemptId={attempt.Id}"));
        }

        return BuildReview(mailing.WithStatus(MailingStatus.Paid), payment);
    }

    private MailingPaymentResult BuildReview(Mailing mailing, Payment? payment)
    {
        try { return MailingPaymentResult.Success(pricing.Calculate(mailing, payment)); }
        catch (InvalidOperationException ex) { return MailingPaymentResult.Failure(ex.Message); }
    }

    private Mailing? GetOwnedMailing(string userEmail, Guid mailingId)
    {
        var normalized = emailNormalizer.Normalize(userEmail);
        return string.IsNullOrWhiteSpace(normalized) ? null : mailings.GetForOwner(mailingId, normalized);
    }
}
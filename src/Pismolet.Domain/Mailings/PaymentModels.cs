namespace Pismolet.Web.Domain.Mailings;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Rub(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Сумма не может быть отрицательной.");
        }

        return new Money(amount, "RUB");
    }

    public override string ToString() => $"{Amount:0.##} {Currency}";
}

public enum PaymentStatus
{
    Pending,
    Paid
}

public static class PaymentStatusLabels
{
    public static string ToRu(this PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "Оплачено",
        _ => "Ожидает оплаты"
    };
}

public enum PaymentAttemptStatus
{
    Pending,
    Succeeded
}

public sealed record PriceSettings(
    Guid Id,
    decimal PricePerRecipient,
    string Currency,
    DateTimeOffset EffectiveFrom,
    bool IsActive)
{
    public static PriceSettings DefaultRub() => new(Guid.NewGuid(), 1m, "RUB", DateTimeOffset.UtcNow, true);

    public void EnsureValid()
    {
        if (PricePerRecipient < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PricePerRecipient), "Цена письма не может быть отрицательной.");
        }

        if (!string.Equals(Currency, "RUB", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Для MVP поддерживается только валюта RUB.", nameof(Currency));
        }
    }
}

public sealed record MailingTariffTier(int MinAcceptedRecipients, int? MaxAcceptedRecipients, decimal PricePerRecipient);

public static class MailingTariff
{
    public const string Currency = "RUB";

    public static IReadOnlyCollection<MailingTariffTier> PublicRubTiers { get; } = new[]
    {
        new MailingTariffTier(0, 299, 1m),
        new MailingTariffTier(300, 499, 0.90m),
        new MailingTariffTier(500, 999, 0.80m),
        new MailingTariffTier(1000, null, 0.70m)
    };

    public static decimal PricePerRecipientFor(int acceptedRecipientsCount)
    {
        if (acceptedRecipientsCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedRecipientsCount), "Количество принятых адресов не может быть отрицательным.");
        }

        var tier = PublicRubTiers.First(x =>
            acceptedRecipientsCount >= x.MinAcceptedRecipients &&
            (x.MaxAcceptedRecipients is null || acceptedRecipientsCount <= x.MaxAcceptedRecipients.Value));
        return tier.PricePerRecipient;
    }

    public static decimal TotalFor(int acceptedRecipientsCount) =>
        acceptedRecipientsCount * PricePerRecipientFor(acceptedRecipientsCount);
}

public sealed record PaymentAttempt(
    Guid Id,
    Guid PaymentId,
    string Provider,
    string ProviderOperationId,
    PaymentAttemptStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string RawCallback)
{
    public const string FakeProvider = "Fake";
    public const string RobokassaFakeProvider = "RobokassaFake";

    public static PaymentAttempt Pending(Guid paymentId, string providerOperationId, string provider = FakeProvider) => new(
        Guid.NewGuid(),
        paymentId,
        provider,
        providerOperationId,
        PaymentAttemptStatus.Pending,
        DateTimeOffset.UtcNow,
        null,
        string.Empty);

    public PaymentAttempt MarkSucceeded(string rawCallback) => this with
    {
        Status = PaymentAttemptStatus.Succeeded,
        CompletedAt = CompletedAt ?? DateTimeOffset.UtcNow,
        RawCallback = string.IsNullOrWhiteSpace(RawCallback) ? rawCallback : RawCallback
    };
}

public sealed record Payment(
    Guid Id,
    Guid MailingId,
    string OwnerEmail,
    int AcceptedRecipientsCount,
    int ExcludedRecipientsCount,
    decimal PricePerRecipient,
    decimal TotalAmount,
    string Currency,
    PaymentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    IReadOnlyCollection<PaymentAttempt> Attempts)
{
    public static Payment Create(
        Guid mailingId,
        string ownerEmail,
        int acceptedRecipientsCount,
        int excludedRecipientsCount,
        PriceSettings priceSettings)
    {
        priceSettings.EnsureValid();

        if (mailingId == Guid.Empty)
        {
            throw new ArgumentException("Не указан идентификатор рассылки.", nameof(mailingId));
        }

        if (acceptedRecipientsCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedRecipientsCount), "Количество принятых адресов не может быть отрицательным.");
        }

        if (excludedRecipientsCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(excludedRecipientsCount), "Количество исключённых адресов не может быть отрицательным.");
        }

        var price = Money.Rub(MailingTariff.PricePerRecipientFor(acceptedRecipientsCount));
        var total = Money.Rub(MailingTariff.TotalFor(acceptedRecipientsCount));

        return new Payment(
            Guid.NewGuid(),
            mailingId,
            ownerEmail.Trim().ToLowerInvariant(),
            acceptedRecipientsCount,
            excludedRecipientsCount,
            price.Amount,
            total.Amount,
            total.Currency,
            PaymentStatus.Pending,
            DateTimeOffset.UtcNow,
            null,
            Array.Empty<PaymentAttempt>());
    }

    public Payment WithAttempt(PaymentAttempt attempt)
    {
        if (Attempts.Any(x => x.ProviderOperationId == attempt.ProviderOperationId))
        {
            return this;
        }

        return this with { Attempts = Attempts.Append(attempt).ToArray() };
    }

    public Payment MarkPaid(PaymentAttempt successfulAttempt)
    {
        if (successfulAttempt.Status != PaymentAttemptStatus.Succeeded)
        {
            throw new InvalidOperationException("Нельзя отметить оплату успешной без успешной платёжной попытки.");
        }

        var attempts = Attempts.Any(x => x.ProviderOperationId == successfulAttempt.ProviderOperationId)
            ? Attempts.Select(x => x.ProviderOperationId == successfulAttempt.ProviderOperationId ? successfulAttempt : x).ToArray()
            : Attempts.Append(successfulAttempt).ToArray();

        return this with
        {
            Status = PaymentStatus.Paid,
            PaidAt = PaidAt ?? successfulAttempt.CompletedAt ?? DateTimeOffset.UtcNow,
            Attempts = attempts
        };
    }
}

using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryPaymentRepository : IPaymentRepository
{
    private readonly ConcurrentDictionary<Guid, Payment> _items = new();

    public Payment? GetByMailingId(Guid mailingId) => _items.GetValueOrDefault(mailingId);

    public Payment? GetByProviderOperationId(string providerOperationId) => _items.Values.FirstOrDefault(payment => payment.Attempts.Any(attempt => attempt.ProviderOperationId == providerOperationId));

    public void Save(Payment payment) => _items[payment.MailingId] = payment;
}

public sealed class InMemoryPriceSettingsRepository : IPriceSettingsRepository
{
    private PriceSettings _settings = PriceSettings.DefaultRub();

    public PriceSettings GetActive() => _settings;

    public void Save(PriceSettings settings)
    {
        settings.EnsureValid();
        _settings = settings with { IsActive = true };
    }
}

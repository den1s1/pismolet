namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed class EmptyMailruPostmasterDashboardReader : IMailruPostmasterDashboardReader
{
    public Task<MailruPostmasterDashboardData> ReadAsync(
        string domain,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default)
    {
        var normalizedDomain = string.IsNullOrWhiteSpace(domain)
            ? "pismolet.ru"
            : domain.Trim().TrimEnd('.').ToLowerInvariant();
        return Task.FromResult(new MailruPostmasterDashboardData(
            normalizedDomain,
            dateFrom,
            dateTo,
            SyncState: null,
            ActiveTroubles: Array.Empty<MailruPostmasterDashboardTrouble>(),
            Days: Array.Empty<MailruPostmasterDashboardDay>()));
    }
}

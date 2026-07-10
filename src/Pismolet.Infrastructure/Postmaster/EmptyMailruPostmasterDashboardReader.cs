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
            Days: Array.Empty<MailruPostmasterDashboardDay>(),
            RecentRuns: Array.Empty<MailruPostmasterDashboardRun>()));
    }
}

public sealed class DisabledMailruPostmasterManualSyncService : IMailruPostmasterManualSyncService
{
    public Task<MailruPostmasterManualSyncResult> RunAsync(
        string? requestedBy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MailruPostmasterManualSyncResult(
            MailruPostmasterManualSyncStatus.Disabled,
            RetryAfterSeconds: null,
            SyncResult: null));
    }
}

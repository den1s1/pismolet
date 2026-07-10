using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed record MailruPostmasterDashboardDay(
    DateOnly Date,
    long MessagesSent,
    long Delivered,
    long ProbablySpam,
    long Spam,
    long Complaints,
    long Read,
    long DeletedRead,
    long DeletedUnread,
    double SpamPercent,
    double ProbablySpamPercent,
    double Reputation,
    double Trend,
    DateTimeOffset UpdatedAt);

public sealed record MailruPostmasterDashboardTrouble(
    int Code,
    string Message,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record MailruPostmasterDashboardSyncState(
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? LastErrorCode,
    string? LastErrorSummary,
    int ConsecutiveFailures,
    DateOnly? LastDomainDate,
    DateTimeOffset UpdatedAt);

public sealed record MailruPostmasterDashboardData(
    string Domain,
    DateOnly DateFrom,
    DateOnly DateTo,
    MailruPostmasterDashboardSyncState? SyncState,
    IReadOnlyList<MailruPostmasterDashboardTrouble> ActiveTroubles,
    IReadOnlyList<MailruPostmasterDashboardDay> Days);

public sealed record MailruPostmasterDashboardSummary(
    int DataDays,
    long MessagesSent,
    long Delivered,
    long ProbablySpam,
    long Spam,
    long Complaints,
    long Read,
    long DeletedRead,
    long DeletedUnread,
    double DeliveredPercent,
    double ProbablySpamPercent,
    double SpamPercent,
    double ComplaintsPercent,
    double ReadPercent,
    double DeletedReadPercent,
    double DeletedUnreadPercent,
    double? LatestReputation,
    double? LatestTrend,
    bool IsLowSample);

public interface IMailruPostmasterDashboardReader
{
    Task<MailruPostmasterDashboardData> ReadAsync(
        string domain,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default);
}

public sealed class EfMailruPostmasterDashboardReader(MailruPostmasterDbContext db) : IMailruPostmasterDashboardReader
{
    public async Task<MailruPostmasterDashboardData> ReadAsync(
        string domain,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default)
    {
        if (dateTo < dateFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(dateTo), "Конец периода Postmaster не может быть раньше начала.");
        }

        var normalizedDomain = NormalizeDomain(domain);
        var stateEntity = await db.SyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Domain == normalizedDomain, cancellationToken);
        var troubleEntities = await db.TroubleSnapshots
            .AsNoTracking()
            .Where(x => x.Domain == normalizedDomain && x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var metricEntities = await db.DomainDailyMetrics
            .AsNoTracking()
            .Where(x => x.Domain == normalizedDomain && x.Date >= dateFrom && x.Date <= dateTo)
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        var state = stateEntity is null
            ? null
            : new MailruPostmasterDashboardSyncState(
                stateEntity.LastAttemptAt,
                stateEntity.LastSuccessAt,
                stateEntity.LastErrorCode,
                stateEntity.LastErrorSummary,
                stateEntity.ConsecutiveFailures,
                stateEntity.LastDomainDate,
                stateEntity.UpdatedAt);
        var troubles = troubleEntities
            .Select(x => new MailruPostmasterDashboardTrouble(
                x.Code,
                x.Message,
                x.FirstSeenAt,
                x.LastSeenAt))
            .ToArray();
        var days = metricEntities
            .Select(x => new MailruPostmasterDashboardDay(
                x.Date,
                x.MessagesSent,
                x.Delivered,
                x.ProbablySpam,
                x.Spam,
                x.Complaints,
                x.Read,
                x.DeletedRead,
                x.DeletedUnread,
                x.SpamPercent,
                x.ProbablySpamPercent,
                x.Reputation,
                x.Trend,
                x.UpdatedAt))
            .ToArray();

        return new MailruPostmasterDashboardData(
            normalizedDomain,
            dateFrom,
            dateTo,
            state,
            troubles,
            days);
    }

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }
}

public static class MailruPostmasterDashboardCalculator
{
    public const long LowSampleMessages = 1000;

    public static MailruPostmasterDashboardSummary Summarize(
        IReadOnlyList<MailruPostmasterDashboardDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        var sent = days.Sum(x => x.MessagesSent);
        var delivered = days.Sum(x => x.Delivered);
        var probablySpam = days.Sum(x => x.ProbablySpam);
        var spam = days.Sum(x => x.Spam);
        var complaints = days.Sum(x => x.Complaints);
        var read = days.Sum(x => x.Read);
        var deletedRead = days.Sum(x => x.DeletedRead);
        var deletedUnread = days.Sum(x => x.DeletedUnread);
        var latest = days.OrderBy(x => x.Date).LastOrDefault();

        return new MailruPostmasterDashboardSummary(
            DataDays: days.Count,
            MessagesSent: sent,
            Delivered: delivered,
            ProbablySpam: probablySpam,
            Spam: spam,
            Complaints: complaints,
            Read: read,
            DeletedRead: deletedRead,
            DeletedUnread: deletedUnread,
            DeliveredPercent: Percent(delivered, sent),
            ProbablySpamPercent: Percent(probablySpam, sent),
            SpamPercent: Percent(spam, sent),
            ComplaintsPercent: Percent(complaints, sent),
            ReadPercent: Percent(read, sent),
            DeletedReadPercent: Percent(deletedRead, sent),
            DeletedUnreadPercent: Percent(deletedUnread, sent),
            LatestReputation: latest?.Reputation,
            LatestTrend: latest?.Trend,
            IsLowSample: sent < LowSampleMessages);
    }

    public static (DateOnly DateFrom, DateOnly DateTo) CalculateCompletedMoscowWindow(
        DateTimeOffset nowUtc,
        int days)
    {
        if (days <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days));
        }

        var moscowNow = TimeZoneInfo.ConvertTime(nowUtc, ResolveMoscowTimeZone());
        var dateTo = DateOnly.FromDateTime(moscowNow.Date).AddDays(-1);
        return (dateTo.AddDays(-(days - 1)), dateTo);
    }

    public static double Percent(long value, long total) =>
        total <= 0 ? 0d : value * 100d / total;

    private static TimeZoneInfo ResolveMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
    }
}

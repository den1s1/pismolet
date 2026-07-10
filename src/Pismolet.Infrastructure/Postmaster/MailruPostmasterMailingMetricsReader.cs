using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Postmaster;

public enum MailruPostmasterMailingMetricsViewStatus
{
    Disabled,
    WaitingForFirstCompletedDay,
    NoData,
    Updating,
    Stable,
    Completed,
    Failed
}

public sealed record MailruPostmasterMailingMetricsDay(
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

public sealed record MailruPostmasterMailingMetricsState(
    DateTimeOffset FirstSendAt,
    DateTimeOffset LastSendAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? LastErrorCode,
    string? LastErrorSummary,
    int ConsecutiveFailures,
    DateOnly? LastDateTo,
    DateTimeOffset? NextAttemptAt,
    bool HasData,
    bool IsStable,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

public sealed record MailruPostmasterMailingMetricsData(
    string Domain,
    Guid MailingId,
    string MsgType,
    MailruPostmasterMailingMetricsState? State,
    IReadOnlyList<MailruPostmasterMailingMetricsDay> Days)
{
    public static MailruPostmasterMailingMetricsData Empty(string domain, Guid mailingId) =>
        new(
            NormalizeDomain(domain),
            EnsureMailingId(mailingId),
            MailruPostmasterMessageType.Build(mailingId),
            State: null,
            Days: Array.Empty<MailruPostmasterMailingMetricsDay>());

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static Guid EnsureMailingId(Guid mailingId)
    {
        if (mailingId == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор рассылки не задан.", nameof(mailingId));
        }

        return mailingId;
    }
}

public sealed record MailruPostmasterMailingMetricsSummary(
    int DataDays,
    DateOnly? DateFrom,
    DateOnly? DateTo,
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
    DateTimeOffset? LastUpdatedAt);

public interface IMailruPostmasterMailingMetricsReader
{
    Task<MailruPostmasterMailingMetricsData> ReadAsync(
        string domain,
        Guid mailingId,
        CancellationToken cancellationToken = default);
}

public sealed class EfMailruPostmasterMailingMetricsReader(MailruPostmasterDbContext db)
    : IMailruPostmasterMailingMetricsReader
{
    public async Task<MailruPostmasterMailingMetricsData> ReadAsync(
        string domain,
        Guid mailingId,
        CancellationToken cancellationToken = default)
    {
        var empty = MailruPostmasterMailingMetricsData.Empty(domain, mailingId);
        var stateEntity = await db.MailingSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Domain == empty.Domain && x.MailingId == mailingId,
                cancellationToken);
        var metricEntities = await db.MailingDailyMetrics
            .AsNoTracking()
            .Where(x => x.Domain == empty.Domain && x.MailingId == mailingId)
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        var state = stateEntity is null
            ? null
            : new MailruPostmasterMailingMetricsState(
                stateEntity.FirstSendAt,
                stateEntity.LastSendAt,
                stateEntity.LastAttemptAt,
                stateEntity.LastSuccessAt,
                stateEntity.LastErrorCode,
                stateEntity.LastErrorSummary,
                stateEntity.ConsecutiveFailures,
                stateEntity.LastDateTo,
                stateEntity.NextAttemptAt,
                stateEntity.HasData,
                stateEntity.IsStable,
                stateEntity.CompletedAt,
                stateEntity.UpdatedAt);
        var days = metricEntities
            .Select(x => new MailruPostmasterMailingMetricsDay(
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
        var msgType = stateEntity?.MsgType
            ?? metricEntities.FirstOrDefault()?.MsgType
            ?? empty.MsgType;

        return new MailruPostmasterMailingMetricsData(
            empty.Domain,
            mailingId,
            msgType,
            state,
            days);
    }
}

public sealed class EmptyMailruPostmasterMailingMetricsReader
    : IMailruPostmasterMailingMetricsReader
{
    public Task<MailruPostmasterMailingMetricsData> ReadAsync(
        string domain,
        Guid mailingId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MailruPostmasterMailingMetricsData.Empty(domain, mailingId));
    }
}

public static class MailruPostmasterMailingMetricsCalculator
{
    public static MailruPostmasterMailingMetricsSummary Summarize(
        IReadOnlyList<MailruPostmasterMailingMetricsDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        var ordered = days.OrderBy(x => x.Date).ToArray();
        var sent = ordered.Sum(x => x.MessagesSent);
        var delivered = ordered.Sum(x => x.Delivered);
        var probablySpam = ordered.Sum(x => x.ProbablySpam);
        var spam = ordered.Sum(x => x.Spam);
        var complaints = ordered.Sum(x => x.Complaints);
        var read = ordered.Sum(x => x.Read);
        var deletedRead = ordered.Sum(x => x.DeletedRead);
        var deletedUnread = ordered.Sum(x => x.DeletedUnread);
        var latest = ordered.LastOrDefault();

        return new MailruPostmasterMailingMetricsSummary(
            DataDays: ordered.Length,
            DateFrom: ordered.FirstOrDefault()?.Date,
            DateTo: latest?.Date,
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
            LastUpdatedAt: ordered.Select(x => (DateTimeOffset?)x.UpdatedAt).Max());
    }

    public static MailruPostmasterMailingMetricsViewStatus ResolveStatus(
        bool integrationEnabled,
        bool mailingMetricsEnabled,
        MailruPostmasterMailingMetricsData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!integrationEnabled || !mailingMetricsEnabled)
        {
            return MailruPostmasterMailingMetricsViewStatus.Disabled;
        }

        var state = data.State;
        if (state is null)
        {
            return MailruPostmasterMailingMetricsViewStatus.WaitingForFirstCompletedDay;
        }

        if (!string.IsNullOrWhiteSpace(state.LastErrorCode))
        {
            return MailruPostmasterMailingMetricsViewStatus.Failed;
        }

        if (state.CompletedAt is not null)
        {
            return MailruPostmasterMailingMetricsViewStatus.Completed;
        }

        if (state.IsStable)
        {
            return MailruPostmasterMailingMetricsViewStatus.Stable;
        }

        if (state.HasData || data.Days.Count > 0)
        {
            return MailruPostmasterMailingMetricsViewStatus.Updating;
        }

        if (state.LastSuccessAt is not null)
        {
            return MailruPostmasterMailingMetricsViewStatus.NoData;
        }

        return MailruPostmasterMailingMetricsViewStatus.WaitingForFirstCompletedDay;
    }

    public static double Percent(long value, long total) =>
        total <= 0 ? 0d : value * 100d / total;
}

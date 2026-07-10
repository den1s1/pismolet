using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed record MailruPostmasterMailingCandidate(
    Guid MailingId,
    MailingStatus Status,
    DateTimeOffset FirstSendAt,
    DateTimeOffset LastSendAt,
    int ProcessedRecipients,
    MailruPostmasterMailingSyncStateEntity? SyncState);

public interface IMailruPostmasterMailingCandidateReader
{
    Task<IReadOnlyList<MailruPostmasterMailingCandidate>> GetCandidatesAsync(
        string domain,
        DateOnly fromDate,
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default);
}

public sealed class EfMailruPostmasterMailingCandidateReader(
    PismoletDbContext applicationDb,
    MailruPostmasterDbContext postmasterDb) : IMailruPostmasterMailingCandidateReader
{
    private static readonly string[] EligibleStatuses =
    [
        MailingStatus.Sending.ToRu(),
        MailingStatus.Sent.ToRu(),
        MailingStatus.Failed.ToRu(),
        MailingStatus.Paused.ToRu()
    ];

    public async Task<IReadOnlyList<MailruPostmasterMailingCandidate>> GetCandidatesAsync(
        string domain,
        DateOnly fromDate,
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var normalizedBatchSize = Math.Clamp(
            batchSize,
            MailruPostmasterMailingMetricsOptions.MinBatchSize,
            MailruPostmasterMailingMetricsOptions.MaxBatchSize);

        var mailings = await applicationDb.Mailings
            .AsNoTracking()
            .Where(x => EligibleStatuses.Contains(x.StatusRu))
            .Select(x => new MailingSeed(x.Id, x.StatusRu))
            .ToArrayAsync(cancellationToken);
        if (mailings.Length == 0)
        {
            return Array.Empty<MailruPostmasterMailingCandidate>();
        }

        var mailingIds = mailings.Select(x => x.MailingId).ToArray();
        var states = await postmasterDb.MailingSyncStates
            .AsNoTracking()
            .Where(x => x.Domain == normalizedDomain && mailingIds.Contains(x.MailingId))
            .ToArrayAsync(cancellationToken);
        var statesByMailing = states.ToDictionary(x => x.MailingId);

        var dueMailings = mailings
            .Where(x => IsDue(statesByMailing.GetValueOrDefault(x.MailingId), nowUtc))
            .ToArray();
        if (dueMailings.Length == 0)
        {
            return Array.Empty<MailruPostmasterMailingCandidate>();
        }

        var dueMailingIds = dueMailings.Select(x => x.MailingId).ToArray();
        var attempts = await applicationDb.SendEvents
            .AsNoTracking()
            .Where(x => x.Attempt > 0 && dueMailingIds.Contains(x.MailingId))
            .Select(x => new MailingAttemptSeed(x.MailingId, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        if (attempts.Length == 0)
        {
            return Array.Empty<MailruPostmasterMailingCandidate>();
        }

        var mailingById = dueMailings.ToDictionary(x => x.MailingId);
        var latestCompletedDate = MailruPostmasterSyncSchedule.GetMoscowDate(nowUtc).AddDays(-1);
        var candidates = attempts
            .GroupBy(x => x.MailingId)
            .Select(group =>
            {
                var orderedAttempts = group
                    .OrderBy(x => x.AttemptAt)
                    .ToArray();
                var mailing = mailingById[group.Key];
                return new MailruPostmasterMailingCandidate(
                    group.Key,
                    MailingStatusLabels.FromRu(mailing.StatusRu),
                    orderedAttempts[0].AttemptAt,
                    orderedAttempts[^1].AttemptAt,
                    orderedAttempts.Length,
                    statesByMailing.GetValueOrDefault(group.Key));
            })
            .Where(x => MailruPostmasterSyncSchedule.GetMoscowDate(x.FirstSendAt) >= fromDate)
            .Where(x => MailruPostmasterSyncSchedule.GetMoscowDate(x.FirstSendAt) <= latestCompletedDate)
            .OrderBy(x => IsOverdue(x.SyncState, nowUtc) ? 0 : 1)
            .ThenBy(x => x.SyncState?.NextAttemptAt ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.SyncState?.LastSuccessAt is null ? 0 : 1)
            .ThenBy(x => x.SyncState?.LastSuccessAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.FirstSendAt)
            .ThenBy(x => x.MailingId)
            .Take(normalizedBatchSize)
            .ToArray();

        return candidates;
    }

    private static bool IsDue(MailruPostmasterMailingSyncStateEntity? state, DateTimeOffset nowUtc) =>
        state is null ||
        (state.CompletedAt is null &&
         (state.NextAttemptAt is null || state.NextAttemptAt <= nowUtc));

    private static bool IsOverdue(MailruPostmasterMailingSyncStateEntity? state, DateTimeOffset nowUtc) =>
        state?.NextAttemptAt is { } nextAttemptAt && nextAttemptAt <= nowUtc;

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private sealed record MailingSeed(Guid MailingId, string StatusRu);

    private sealed record MailingAttemptSeed(Guid MailingId, DateTimeOffset AttemptAt);
}

public sealed class EmptyMailruPostmasterMailingCandidateReader : IMailruPostmasterMailingCandidateReader
{
    public Task<IReadOnlyList<MailruPostmasterMailingCandidate>> GetCandidatesAsync(
        string domain,
        DateOnly fromDate,
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MailruPostmasterMailingCandidate>>(
            Array.Empty<MailruPostmasterMailingCandidate>());
}

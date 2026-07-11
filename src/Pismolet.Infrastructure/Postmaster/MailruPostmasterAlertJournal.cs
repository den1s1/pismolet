namespace Pismolet.Web.Infrastructure.Postmaster;

public enum MailruPostmasterAlertEventStatus
{
    Active = 0,
    Resolved = 1
}

public enum MailruPostmasterAlertJournalChangeType
{
    Activated = 0,
    Updated = 1,
    Resolved = 2
}

public sealed record MailruPostmasterAlertJournalEvent(
    Guid Id,
    string Domain,
    string Code,
    MailruPostmasterAlertSeverity Severity,
    string Category,
    MailruPostmasterAlertEventStatus Status,
    string Fingerprint,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    long MessagesSent,
    double ObservedValue,
    double ThresholdValue,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    int OccurrenceCount,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? LastNotifiedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MailruPostmasterAlertJournalChange(
    MailruPostmasterAlertJournalChangeType ChangeType,
    MailruPostmasterAlertJournalEvent Event);

public sealed record MailruPostmasterAlertJournalReconcileResult(
    IReadOnlyList<MailruPostmasterAlertJournalChange> Changes)
{
    public IReadOnlyList<MailruPostmasterAlertJournalChange> Activated =>
        Changes.Where(x => x.ChangeType == MailruPostmasterAlertJournalChangeType.Activated).ToArray();

    public IReadOnlyList<MailruPostmasterAlertJournalChange> Updated =>
        Changes.Where(x => x.ChangeType == MailruPostmasterAlertJournalChangeType.Updated).ToArray();

    public IReadOnlyList<MailruPostmasterAlertJournalChange> Resolved =>
        Changes.Where(x => x.ChangeType == MailruPostmasterAlertJournalChangeType.Resolved).ToArray();
}

public interface IMailruPostmasterAlertJournalReconciler
{
    MailruPostmasterAlertJournalReconcileResult Reconcile(
        string domain,
        MailruPostmasterAlertEvaluation evaluation,
        IReadOnlyList<MailruPostmasterAlertJournalEvent> activeEvents,
        DateTimeOffset nowUtc,
        bool alertsEnabled);
}

public sealed class MailruPostmasterAlertJournalReconciler : IMailruPostmasterAlertJournalReconciler
{
    public MailruPostmasterAlertJournalReconcileResult Reconcile(
        string domain,
        MailruPostmasterAlertEvaluation evaluation,
        IReadOnlyList<MailruPostmasterAlertJournalEvent> activeEvents,
        DateTimeOffset nowUtc,
        bool alertsEnabled)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(activeEvents);

        var normalizedDomain = NormalizeDomain(domain);
        if (!alertsEnabled || evaluation.Status == MailruPostmasterAlertOverallStatus.Off)
        {
            return new MailruPostmasterAlertJournalReconcileResult(
                Array.Empty<MailruPostmasterAlertJournalChange>());
        }

        var existingByKey = activeEvents
            .Where(x => x.Status == MailruPostmasterAlertEventStatus.Active)
            .Where(x => string.Equals(NormalizeDomain(x.Domain), normalizedDomain, StringComparison.Ordinal))
            .GroupBy(x => BuildKey(x.Code, x.Fingerprint), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(item => item.LastObservedAt).First(), StringComparer.Ordinal);

        var currentByKey = evaluation.Signals
            .GroupBy(x => BuildKey(x.Code, x.Fingerprint), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var changes = new List<MailruPostmasterAlertJournalChange>();

        foreach (var pair in currentByKey.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (existingByKey.TryGetValue(pair.Key, out var existing))
            {
                changes.Add(new MailruPostmasterAlertJournalChange(
                    MailruPostmasterAlertJournalChangeType.Updated,
                    Update(existing, pair.Value, nowUtc)));
                continue;
            }

            changes.Add(new MailruPostmasterAlertJournalChange(
                MailruPostmasterAlertJournalChangeType.Activated,
                Activate(normalizedDomain, pair.Value, nowUtc)));
        }

        foreach (var pair in existingByKey
                     .Where(x => !currentByKey.ContainsKey(x.Key))
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            changes.Add(new MailruPostmasterAlertJournalChange(
                MailruPostmasterAlertJournalChangeType.Resolved,
                Resolve(pair.Value, nowUtc)));
        }

        return new MailruPostmasterAlertJournalReconcileResult(changes);
    }

    private static MailruPostmasterAlertJournalEvent Activate(
        string domain,
        MailruPostmasterAlertSignal signal,
        DateTimeOffset nowUtc) => new(
        Guid.NewGuid(),
        domain,
        NormalizeCode(signal.Code),
        signal.Severity,
        NormalizeCategory(signal.Category),
        MailruPostmasterAlertEventStatus.Active,
        NormalizeFingerprint(signal.Fingerprint),
        signal.DateFrom,
        signal.DateTo,
        Math.Max(0, signal.MessagesSent),
        signal.ObservedValue,
        signal.ThresholdValue,
        nowUtc,
        nowUtc,
        1,
        null,
        null,
        nowUtc,
        nowUtc);

    private static MailruPostmasterAlertJournalEvent Update(
        MailruPostmasterAlertJournalEvent existing,
        MailruPostmasterAlertSignal signal,
        DateTimeOffset nowUtc) => existing with
        {
            Severity = signal.Severity,
            Category = NormalizeCategory(signal.Category),
            DateFrom = signal.DateFrom,
            DateTo = signal.DateTo,
            MessagesSent = Math.Max(0, signal.MessagesSent),
            ObservedValue = signal.ObservedValue,
            ThresholdValue = signal.ThresholdValue,
            LastObservedAt = nowUtc,
            OccurrenceCount = existing.OccurrenceCount >= int.MaxValue
                ? int.MaxValue
                : Math.Max(0, existing.OccurrenceCount) + 1,
            ResolvedAt = null,
            UpdatedAt = nowUtc
        };

    private static MailruPostmasterAlertJournalEvent Resolve(
        MailruPostmasterAlertJournalEvent existing,
        DateTimeOffset nowUtc) => existing with
        {
            Status = MailruPostmasterAlertEventStatus.Resolved,
            ResolvedAt = nowUtc,
            UpdatedAt = nowUtc
        };

    private static string BuildKey(string code, string fingerprint) =>
        $"{NormalizeCode(code)}|{NormalizeFingerprint(fingerprint)}";

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Код сигнала PM-5 не задан.", nameof(code));
        }

        return code.Trim().ToLowerInvariant();
    }

    private static string NormalizeCategory(string category) =>
        string.IsNullOrWhiteSpace(category) ? "unknown" : category.Trim().ToLowerInvariant();

    private static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new ArgumentException("Fingerprint сигнала PM-5 не задан.", nameof(fingerprint));
        }

        return fingerprint.Trim().ToLowerInvariant();
    }
}

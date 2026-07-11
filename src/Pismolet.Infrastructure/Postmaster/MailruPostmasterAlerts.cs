using System.Security.Cryptography;
using System.Text;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed class MailruPostmasterAlertOptions
{
    public bool Enabled { get; set; }
    public bool ObservationMode { get; set; } = true;
    public DateTimeOffset? ObservationStartedAt { get; set; }
    public int WindowDays { get; set; } = 7;
    public long MinimumMessagesForRates { get; set; } = 100;
    public long SpamCriticalCount { get; set; } = 1;
    public long ComplaintsWarningCount { get; set; } = 1;
    public double ComplaintsCriticalPercent { get; set; } = 0.3d;
    public double ProbablySpamWarningPercent { get; set; } = 10d;
    public double ProbablySpamGrowthPoints { get; set; } = 5d;
    public int SyncStaleHours { get; set; } = 48;
    public int ConsecutiveFailures { get; set; } = 3;
    public int NotificationCooldownHours { get; set; } = 24;
    public bool NotificationsEnabled { get; set; }

    public MailruPostmasterAlertOptions Normalize() => new()
    {
        Enabled = Enabled,
        ObservationMode = ObservationMode,
        ObservationStartedAt = ObservationStartedAt,
        WindowDays = Math.Clamp(WindowDays, 1, 30),
        MinimumMessagesForRates = Math.Clamp(MinimumMessagesForRates, 1, 1_000_000),
        SpamCriticalCount = Math.Clamp(SpamCriticalCount, 1, 1_000_000),
        ComplaintsWarningCount = Math.Clamp(ComplaintsWarningCount, 1, 1_000_000),
        ComplaintsCriticalPercent = Math.Clamp(ComplaintsCriticalPercent, 0d, 100d),
        ProbablySpamWarningPercent = Math.Clamp(ProbablySpamWarningPercent, 0d, 100d),
        ProbablySpamGrowthPoints = Math.Clamp(ProbablySpamGrowthPoints, 0d, 100d),
        SyncStaleHours = Math.Clamp(SyncStaleHours, 1, 168),
        ConsecutiveFailures = Math.Clamp(ConsecutiveFailures, 1, 100),
        NotificationCooldownHours = Math.Clamp(NotificationCooldownHours, 1, 168),
        NotificationsEnabled = NotificationsEnabled
    };
}

public enum MailruPostmasterAlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum MailruPostmasterAlertOverallStatus
{
    Off = 0,
    NoData = 1,
    Calm = 2,
    Attention = 3,
    Critical = 4
}

public sealed record MailruPostmasterAlertInput(
    string Domain,
    bool IntegrationEnabled,
    bool IntegrationConfigured,
    DateOnly CurrentDateFrom,
    DateOnly CurrentDateTo,
    IReadOnlyList<MailruPostmasterDashboardDay> CurrentDays,
    DateOnly PreviousDateFrom,
    DateOnly PreviousDateTo,
    IReadOnlyList<MailruPostmasterDashboardDay> PreviousDays,
    IReadOnlyList<MailruPostmasterDashboardTrouble> ActiveTroubles,
    MailruPostmasterDashboardSyncState? SyncState);

public sealed record MailruPostmasterAlertSignal(
    string Code,
    MailruPostmasterAlertSeverity Severity,
    string Category,
    string Title,
    string Summary,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    long MessagesSent,
    double ObservedValue,
    double ThresholdValue,
    bool IsLowSample,
    bool IsObservation,
    string Source,
    string Fingerprint);

public sealed record MailruPostmasterAlertEvaluation(
    MailruPostmasterAlertOverallStatus Status,
    bool IsObservation,
    bool IsLowSample,
    long MessagesSent,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<MailruPostmasterAlertSignal> Signals);

public interface IMailruPostmasterAlertEvaluator
{
    MailruPostmasterAlertEvaluation Evaluate(
        MailruPostmasterAlertInput input,
        MailruPostmasterAlertOptions options,
        DateTimeOffset nowUtc);
}

public sealed class MailruPostmasterAlertEvaluator : IMailruPostmasterAlertEvaluator
{
    public MailruPostmasterAlertEvaluation Evaluate(
        MailruPostmasterAlertInput input,
        MailruPostmasterAlertOptions options,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var normalizedOptions = options.Normalize();
        var normalizedDomain = NormalizeDomain(input.Domain);
        if (!normalizedOptions.Enabled)
        {
            return new MailruPostmasterAlertEvaluation(
                MailruPostmasterAlertOverallStatus.Off,
                normalizedOptions.ObservationMode,
                true,
                0,
                nowUtc,
                Array.Empty<MailruPostmasterAlertSignal>());
        }

        var current = Aggregate(input.CurrentDays);
        var previous = Aggregate(input.PreviousDays);
        var isLowSample = current.MessagesSent < normalizedOptions.MinimumMessagesForRates;
        var signals = new List<MailruPostmasterAlertSignal>();

        AddAuthenticationSignals(
            signals,
            normalizedDomain,
            input.ActiveTroubles,
            normalizedOptions.ObservationMode);

        AddSpamSignal(
            signals,
            normalizedDomain,
            input,
            current,
            normalizedOptions,
            isLowSample);

        AddComplaintsSignal(
            signals,
            normalizedDomain,
            input,
            current,
            normalizedOptions,
            isLowSample);

        AddProbablySpamSignals(
            signals,
            normalizedDomain,
            input,
            current,
            previous,
            normalizedOptions,
            isLowSample);

        AddSynchronizationSignals(
            signals,
            normalizedDomain,
            input,
            normalizedOptions,
            nowUtc);

        var status = ResolveStatus(signals, current.MessagesSent, input.IntegrationEnabled, input.IntegrationConfigured);
        return new MailruPostmasterAlertEvaluation(
            status,
            normalizedOptions.ObservationMode,
            isLowSample,
            current.MessagesSent,
            nowUtc,
            signals
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.Code, StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddAuthenticationSignals(
        ICollection<MailruPostmasterAlertSignal> signals,
        string domain,
        IReadOnlyList<MailruPostmasterDashboardTrouble> troubles,
        bool isObservation)
    {
        foreach (var trouble in troubles.OrderBy(x => x.Code))
        {
            var code = $"authentication_trouble_{trouble.Code}";
            signals.Add(CreateSignal(
                domain,
                code,
                MailruPostmasterAlertSeverity.Critical,
                "authentication",
                "Проблема аутентификации домена",
                string.IsNullOrWhiteSpace(trouble.Message)
                    ? $"Mail.ru сообщает о проблеме проверки домена, код {trouble.Code}."
                    : trouble.Message.Trim(),
                null,
                null,
                0,
                trouble.Code,
                0,
                false,
                isObservation,
                "trouble_snapshots",
                trouble.Code.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private static void AddSpamSignal(
        ICollection<MailruPostmasterAlertSignal> signals,
        string domain,
        MailruPostmasterAlertInput input,
        MetricAggregate current,
        MailruPostmasterAlertOptions options,
        bool isLowSample)
    {
        if (current.Spam < options.SpamCriticalCount)
        {
            return;
        }

        signals.Add(CreateSignal(
            domain,
            "spam_detected",
            MailruPostmasterAlertSeverity.Critical,
            "spam",
            "Mail.ru отметил письма как спам",
            $"За период отмечено как спам: {current.Spam} из {current.MessagesSent} отправленных писем ({Percent(current.Spam, current.MessagesSent):0.###}%).",
            input.CurrentDateFrom,
            input.CurrentDateTo,
            current.MessagesSent,
            current.Spam,
            options.SpamCriticalCount,
            isLowSample,
            options.ObservationMode,
            "domain_daily_metrics",
            $"{input.CurrentDateFrom:yyyy-MM-dd}|{input.CurrentDateTo:yyyy-MM-dd}"));
    }

    private static void AddComplaintsSignal(
        ICollection<MailruPostmasterAlertSignal> signals,
        string domain,
        MailruPostmasterAlertInput input,
        MetricAggregate current,
        MailruPostmasterAlertOptions options,
        bool isLowSample)
    {
        if (current.Complaints < options.ComplaintsWarningCount)
        {
            return;
        }

        var complaintsPercent = Percent(current.Complaints, current.MessagesSent);
        var severity = !isLowSample && complaintsPercent >= options.ComplaintsCriticalPercent
            ? MailruPostmasterAlertSeverity.Critical
            : MailruPostmasterAlertSeverity.Warning;

        signals.Add(CreateSignal(
            domain,
            "complaints_detected",
            severity,
            "complaints",
            "Получены жалобы на рассылки",
            $"За период получено жалоб: {current.Complaints} на {current.MessagesSent} отправленных писем ({complaintsPercent:0.###}%).",
            input.CurrentDateFrom,
            input.CurrentDateTo,
            current.MessagesSent,
            current.Complaints,
            severity == MailruPostmasterAlertSeverity.Critical
                ? options.ComplaintsCriticalPercent
                : options.ComplaintsWarningCount,
            isLowSample,
            options.ObservationMode,
            "domain_daily_metrics",
            $"{input.CurrentDateFrom:yyyy-MM-dd}|{input.CurrentDateTo:yyyy-MM-dd}"));
    }

    private static void AddProbablySpamSignals(
        ICollection<MailruPostmasterAlertSignal> signals,
        string domain,
        MailruPostmasterAlertInput input,
        MetricAggregate current,
        MetricAggregate previous,
        MailruPostmasterAlertOptions options,
        bool isLowSample)
    {
        if (isLowSample)
        {
            return;
        }

        var currentPercent = Percent(current.ProbablySpam, current.MessagesSent);
        if (currentPercent >= options.ProbablySpamWarningPercent)
        {
            signals.Add(CreateSignal(
                domain,
                "probably_spam_high",
                MailruPostmasterAlertSeverity.Warning,
                "probably_spam",
                "Высокая доля писем в категории «возможно спам»",
                $"За период в категорию «возможно спам» попало {current.ProbablySpam} из {current.MessagesSent} писем ({currentPercent:0.###}%).",
                input.CurrentDateFrom,
                input.CurrentDateTo,
                current.MessagesSent,
                currentPercent,
                options.ProbablySpamWarningPercent,
                false,
                options.ObservationMode,
                "domain_daily_metrics",
                $"{input.CurrentDateFrom:yyyy-MM-dd}|{input.CurrentDateTo:yyyy-MM-dd}"));
        }

        if (previous.MessagesSent < options.MinimumMessagesForRates)
        {
            return;
        }

        var previousPercent = Percent(previous.ProbablySpam, previous.MessagesSent);
        var growth = currentPercent - previousPercent;
        if (currentPercent < options.ProbablySpamWarningPercent || growth < options.ProbablySpamGrowthPoints)
        {
            return;
        }

        signals.Add(CreateSignal(
            domain,
            "probably_spam_growth",
            MailruPostmasterAlertSeverity.Warning,
            "probably_spam",
            "Выросла доля писем в категории «возможно спам»",
            $"Доля выросла с {previousPercent:0.###}% до {currentPercent:0.###}% — на {growth:0.###} процентного пункта.",
            input.CurrentDateFrom,
            input.CurrentDateTo,
            current.MessagesSent,
            growth,
            options.ProbablySpamGrowthPoints,
            false,
            options.ObservationMode,
            "domain_daily_metrics",
            $"{input.PreviousDateFrom:yyyy-MM-dd}|{input.PreviousDateTo:yyyy-MM-dd}|{input.CurrentDateFrom:yyyy-MM-dd}|{input.CurrentDateTo:yyyy-MM-dd}"));
    }

    private static void AddSynchronizationSignals(
        ICollection<MailruPostmasterAlertSignal> signals,
        string domain,
        MailruPostmasterAlertInput input,
        MailruPostmasterAlertOptions options,
        DateTimeOffset nowUtc)
    {
        if (!input.IntegrationEnabled || !input.IntegrationConfigured)
        {
            return;
        }

        if (input.SyncState?.LastSuccessAt is null)
        {
            signals.Add(CreateSignal(
                domain,
                "sync_waiting_first_success",
                MailruPostmasterAlertSeverity.Info,
                "synchronization",
                "Ожидается первая успешная синхронизация",
                "Данных Mail.ru пока недостаточно для оценки доставляемости.",
                null,
                null,
                0,
                0,
                0,
                true,
                options.ObservationMode,
                "sync_states",
                "first_success"));
            return;
        }

        var staleAfter = TimeSpan.FromHours(options.SyncStaleHours);
        var age = nowUtc - input.SyncState.LastSuccessAt.Value;
        if (age >= staleAfter)
        {
            signals.Add(CreateSignal(
                domain,
                "sync_stale",
                MailruPostmasterAlertSeverity.Warning,
                "synchronization",
                "Данные Mail.ru давно не обновлялись",
                $"С последней успешной синхронизации прошло {Math.Floor(age.TotalHours):0} ч. Порог: {options.SyncStaleHours} ч.",
                null,
                null,
                0,
                age.TotalHours,
                options.SyncStaleHours,
                false,
                options.ObservationMode,
                "sync_states",
                input.SyncState.LastSuccessAt.Value.ToUniversalTime().ToString("O")));
        }

        if (input.SyncState.ConsecutiveFailures >= options.ConsecutiveFailures)
        {
            var errorCode = string.IsNullOrWhiteSpace(input.SyncState.LastErrorCode)
                ? "sync_error"
                : input.SyncState.LastErrorCode.Trim();
            signals.Add(CreateSignal(
                domain,
                "sync_consecutive_failures",
                MailruPostmasterAlertSeverity.Warning,
                "synchronization",
                "Повторяются ошибки синхронизации Mail.ru",
                $"Ошибок подряд: {input.SyncState.ConsecutiveFailures}. Код последней ошибки: {errorCode}.",
                null,
                null,
                0,
                input.SyncState.ConsecutiveFailures,
                options.ConsecutiveFailures,
                false,
                options.ObservationMode,
                "sync_states",
                errorCode));
        }
    }

    private static MailruPostmasterAlertOverallStatus ResolveStatus(
        IReadOnlyCollection<MailruPostmasterAlertSignal> signals,
        long messagesSent,
        bool integrationEnabled,
        bool integrationConfigured)
    {
        if (!integrationEnabled || !integrationConfigured)
        {
            return MailruPostmasterAlertOverallStatus.NoData;
        }

        if (signals.Any(x => x.Severity == MailruPostmasterAlertSeverity.Critical))
        {
            return MailruPostmasterAlertOverallStatus.Critical;
        }

        if (signals.Any(x => x.Severity == MailruPostmasterAlertSeverity.Warning))
        {
            return MailruPostmasterAlertOverallStatus.Attention;
        }

        return messagesSent > 0
            ? MailruPostmasterAlertOverallStatus.Calm
            : MailruPostmasterAlertOverallStatus.NoData;
    }

    private static MailruPostmasterAlertSignal CreateSignal(
        string domain,
        string code,
        MailruPostmasterAlertSeverity severity,
        string category,
        string title,
        string summary,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        long messagesSent,
        double observedValue,
        double thresholdValue,
        bool isLowSample,
        bool isObservation,
        string source,
        string fingerprintSource)
    {
        var fingerprint = BuildFingerprint(domain, code, source, fingerprintSource);
        return new MailruPostmasterAlertSignal(
            code,
            severity,
            category,
            title,
            summary,
            dateFrom,
            dateTo,
            messagesSent,
            observedValue,
            thresholdValue,
            isLowSample,
            isObservation,
            source,
            fingerprint);
    }

    private static string BuildFingerprint(params string[] parts)
    {
        var normalized = string.Join('|', parts.Select(x => x.Trim().ToLowerInvariant()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static MetricAggregate Aggregate(IReadOnlyList<MailruPostmasterDashboardDay> days) => new(
        days.Sum(x => x.MessagesSent),
        days.Sum(x => x.ProbablySpam),
        days.Sum(x => x.Spam),
        days.Sum(x => x.Complaints));

    private static double Percent(long value, long total) =>
        total <= 0 ? 0d : value * 100d / total;

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private sealed record MetricAggregate(
        long MessagesSent,
        long ProbablySpam,
        long Spam,
        long Complaints);
}

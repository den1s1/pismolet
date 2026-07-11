using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterAlertEvaluatorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly CurrentFrom = new(2026, 7, 5);
    private static readonly DateOnly CurrentTo = new(2026, 7, 11);
    private static readonly DateOnly PreviousFrom = new(2026, 6, 28);
    private static readonly DateOnly PreviousTo = new(2026, 7, 4);

    [Fact]
    public void Evaluate_WhenDisabled_ReturnsOffWithoutSignals()
    {
        var result = Evaluate(
            CurrentDays(Day(CurrentTo, sent: 100, probablySpam: 0, spam: 0, complaints: 0)),
            options: new MailruPostmasterAlertOptions { Enabled = false });

        Assert.Equal(MailruPostmasterAlertOverallStatus.Off, result.Status);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public void Evaluate_EmptyMetrics_ReturnsNoDataWithoutFalseCalmStatus()
    {
        var result = Evaluate();

        Assert.Equal(MailruPostmasterAlertOverallStatus.NoData, result.Status);
        Assert.True(result.IsLowSample);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public void Evaluate_SmallSampleSuppressesRateRulesButKeepsAbsoluteSpamAndComplaints()
    {
        var result = Evaluate(
            CurrentDays(Day(CurrentTo, sent: 20, probablySpam: 20, spam: 1, complaints: 1)),
            options: EnabledOptions(minimumMessagesForRates: 100));

        Assert.Equal(MailruPostmasterAlertOverallStatus.Critical, result.Status);
        Assert.Contains(result.Signals, x => x.Code == "spam_detected" && x.Severity == MailruPostmasterAlertSeverity.Critical);
        Assert.Contains(result.Signals, x => x.Code == "complaints_detected" && x.Severity == MailruPostmasterAlertSeverity.Warning);
        Assert.DoesNotContain(result.Signals, x => x.Code == "probably_spam_high");
        Assert.DoesNotContain(result.Signals, x => x.Code == "probably_spam_growth");
        Assert.True(result.IsLowSample);
    }

    [Fact]
    public void Evaluate_ComplaintsBecomeCriticalOnlyWithEnoughVolumeAndPercentThreshold()
    {
        var result = Evaluate(
            CurrentDays(Day(CurrentTo, sent: 1_000, probablySpam: 0, spam: 0, complaints: 3)),
            options: EnabledOptions(minimumMessagesForRates: 100, complaintsCriticalPercent: 0.3d));

        var signal = Assert.Single(result.Signals, x => x.Code == "complaints_detected");
        Assert.Equal(MailruPostmasterAlertSeverity.Critical, signal.Severity);
        Assert.False(signal.IsLowSample);
        Assert.Equal(3, signal.ObservedValue);
    }

    [Fact]
    public void Evaluate_HighProbablySpamUsesWeightedAbsoluteTotals()
    {
        var result = Evaluate(
            CurrentDays(
                Day(new DateOnly(2026, 7, 10), sent: 10, probablySpam: 10, spam: 0, complaints: 0),
                Day(CurrentTo, sent: 990, probablySpam: 90, spam: 0, complaints: 0)),
            options: EnabledOptions(minimumMessagesForRates: 100, probablySpamWarningPercent: 10d));

        var signal = Assert.Single(result.Signals, x => x.Code == "probably_spam_high");
        Assert.Equal(10d, signal.ObservedValue, precision: 6);
        Assert.Equal(1_000, signal.MessagesSent);
        Assert.Equal(MailruPostmasterAlertOverallStatus.Attention, result.Status);
    }

    [Fact]
    public void Evaluate_ProbablySpamGrowthComparesTwoSufficientWindowsInPercentagePoints()
    {
        var result = Evaluate(
            CurrentDays(Day(CurrentTo, sent: 1_000, probablySpam: 150, spam: 0, complaints: 0)),
            PreviousDays(Day(PreviousTo, sent: 1_000, probablySpam: 50, spam: 0, complaints: 0)),
            options: EnabledOptions(
                minimumMessagesForRates: 100,
                probablySpamWarningPercent: 10d,
                probablySpamGrowthPoints: 5d));

        var signal = Assert.Single(result.Signals, x => x.Code == "probably_spam_growth");
        Assert.Equal(10d, signal.ObservedValue, precision: 6);
        Assert.Equal(5d, signal.ThresholdValue, precision: 6);
    }

    [Fact]
    public void Evaluate_ProbablySpamGrowthRequiresEnoughVolumeInPreviousWindow()
    {
        var result = Evaluate(
            CurrentDays(Day(CurrentTo, sent: 1_000, probablySpam: 150, spam: 0, complaints: 0)),
            PreviousDays(Day(PreviousTo, sent: 99, probablySpam: 0, spam: 0, complaints: 0)),
            options: EnabledOptions(
                minimumMessagesForRates: 100,
                probablySpamWarningPercent: 10d,
                probablySpamGrowthPoints: 5d));

        Assert.Contains(result.Signals, x => x.Code == "probably_spam_high");
        Assert.DoesNotContain(result.Signals, x => x.Code == "probably_spam_growth");
    }

    [Fact]
    public void Evaluate_ActiveAuthenticationTroubleCreatesCriticalSignal()
    {
        var trouble = new MailruPostmasterDashboardTrouble(
            101,
            "DKIM problem",
            NowUtc.AddHours(-1),
            NowUtc);

        var result = Evaluate(
            CurrentDays(Day(CurrentTo, sent: 100, probablySpam: 0, spam: 0, complaints: 0)),
            troubles: [trouble]);

        var signal = Assert.Single(result.Signals, x => x.Code == "authentication_trouble_101");
        Assert.Equal(MailruPostmasterAlertSeverity.Critical, signal.Severity);
        Assert.Equal("authentication", signal.Category);
        Assert.Equal(MailruPostmasterAlertOverallStatus.Critical, result.Status);
    }

    [Fact]
    public void Evaluate_StaleSyncAndFailureThresholdCreateTechnicalWarnings()
    {
        var state = SyncState(
            lastSuccessAt: NowUtc.AddHours(-49),
            consecutiveFailures: 3,
            lastErrorCode: "http_500");

        var result = Evaluate(
            CurrentDays(Day(CurrentTo, sent: 100, probablySpam: 0, spam: 0, complaints: 0)),
            syncState: state,
            options: EnabledOptions(syncStaleHours: 48, consecutiveFailures: 3));

        Assert.Contains(result.Signals, x => x.Code == "sync_stale" && x.Severity == MailruPostmasterAlertSeverity.Warning);
        Assert.Contains(result.Signals, x => x.Code == "sync_consecutive_failures" && x.Severity == MailruPostmasterAlertSeverity.Warning);
        Assert.Equal(MailruPostmasterAlertOverallStatus.Attention, result.Status);
    }

    [Fact]
    public void Evaluate_WithoutFirstSuccessCreatesInfoSignalButNotQualityWarning()
    {
        var state = new MailruPostmasterDashboardSyncState(
            LastAttemptAt: NowUtc,
            LastSuccessAt: null,
            LastErrorCode: null,
            LastErrorSummary: null,
            ConsecutiveFailures: 0,
            LastDomainDate: null,
            UpdatedAt: NowUtc);

        var result = Evaluate(syncState: state);

        var signal = Assert.Single(result.Signals);
        Assert.Equal("sync_waiting_first_success", signal.Code);
        Assert.Equal(MailruPostmasterAlertSeverity.Info, signal.Severity);
        Assert.Equal(MailruPostmasterAlertOverallStatus.NoData, result.Status);
    }

    [Fact]
    public void Evaluate_IdenticalInputProducesStableFingerprints()
    {
        var current = CurrentDays(Day(CurrentTo, sent: 100, probablySpam: 20, spam: 1, complaints: 1));

        var first = Evaluate(current);
        var second = Evaluate(current);

        Assert.Equal(
            first.Signals.Select(x => (x.Code, x.Fingerprint)),
            second.Signals.Select(x => (x.Code, x.Fingerprint)));
        Assert.All(first.Signals, x => Assert.Equal(64, x.Fingerprint.Length));
    }

    [Fact]
    public void Normalize_ClampsUnsafeConfigurationValues()
    {
        var normalized = new MailruPostmasterAlertOptions
        {
            WindowDays = 0,
            MinimumMessagesForRates = 0,
            SpamCriticalCount = 0,
            ComplaintsWarningCount = 0,
            ComplaintsCriticalPercent = -1,
            ProbablySpamWarningPercent = 101,
            ProbablySpamGrowthPoints = 101,
            SyncStaleHours = 0,
            ConsecutiveFailures = 0,
            NotificationCooldownHours = 0
        }.Normalize();

        Assert.Equal(1, normalized.WindowDays);
        Assert.Equal(1, normalized.MinimumMessagesForRates);
        Assert.Equal(1, normalized.SpamCriticalCount);
        Assert.Equal(1, normalized.ComplaintsWarningCount);
        Assert.Equal(0d, normalized.ComplaintsCriticalPercent);
        Assert.Equal(100d, normalized.ProbablySpamWarningPercent);
        Assert.Equal(100d, normalized.ProbablySpamGrowthPoints);
        Assert.Equal(1, normalized.SyncStaleHours);
        Assert.Equal(1, normalized.ConsecutiveFailures);
        Assert.Equal(1, normalized.NotificationCooldownHours);
    }

    private static MailruPostmasterAlertEvaluation Evaluate(
        IReadOnlyList<MailruPostmasterDashboardDay>? currentDays = null,
        IReadOnlyList<MailruPostmasterDashboardDay>? previousDays = null,
        IReadOnlyList<MailruPostmasterDashboardTrouble>? troubles = null,
        MailruPostmasterDashboardSyncState? syncState = null,
        MailruPostmasterAlertOptions? options = null,
        bool integrationEnabled = true,
        bool integrationConfigured = true)
    {
        var input = new MailruPostmasterAlertInput(
            Domain: "pismolet.ru",
            IntegrationEnabled: integrationEnabled,
            IntegrationConfigured: integrationConfigured,
            CurrentDateFrom: CurrentFrom,
            CurrentDateTo: CurrentTo,
            CurrentDays: currentDays ?? Array.Empty<MailruPostmasterDashboardDay>(),
            PreviousDateFrom: PreviousFrom,
            PreviousDateTo: PreviousTo,
            PreviousDays: previousDays ?? Array.Empty<MailruPostmasterDashboardDay>(),
            ActiveTroubles: troubles ?? Array.Empty<MailruPostmasterDashboardTrouble>(),
            SyncState: syncState ?? SyncState(NowUtc.AddHours(-1), 0));

        return new MailruPostmasterAlertEvaluator().Evaluate(
            input,
            options ?? EnabledOptions(),
            NowUtc);
    }

    private static MailruPostmasterAlertOptions EnabledOptions(
        long minimumMessagesForRates = 100,
        double complaintsCriticalPercent = 0.3d,
        double probablySpamWarningPercent = 10d,
        double probablySpamGrowthPoints = 5d,
        int syncStaleHours = 48,
        int consecutiveFailures = 3) => new()
        {
            Enabled = true,
            ObservationMode = true,
            MinimumMessagesForRates = minimumMessagesForRates,
            ComplaintsCriticalPercent = complaintsCriticalPercent,
            ProbablySpamWarningPercent = probablySpamWarningPercent,
            ProbablySpamGrowthPoints = probablySpamGrowthPoints,
            SyncStaleHours = syncStaleHours,
            ConsecutiveFailures = consecutiveFailures
        };

    private static IReadOnlyList<MailruPostmasterDashboardDay> CurrentDays(params MailruPostmasterDashboardDay[] days) => days;

    private static IReadOnlyList<MailruPostmasterDashboardDay> PreviousDays(params MailruPostmasterDashboardDay[] days) => days;

    private static MailruPostmasterDashboardDay Day(
        DateOnly date,
        long sent,
        long probablySpam,
        long spam,
        long complaints) => new(
            Date: date,
            MessagesSent: sent,
            Delivered: Math.Max(0, sent - probablySpam - spam),
            ProbablySpam: probablySpam,
            Spam: spam,
            Complaints: complaints,
            Read: 0,
            DeletedRead: 0,
            DeletedUnread: 0,
            SpamPercent: sent == 0 ? 0 : spam * 100d / sent,
            ProbablySpamPercent: sent == 0 ? 0 : probablySpam * 100d / sent,
            Reputation: 0,
            Trend: 0,
            UpdatedAt: NowUtc);

    private static MailruPostmasterDashboardSyncState SyncState(
        DateTimeOffset? lastSuccessAt,
        int consecutiveFailures,
        string? lastErrorCode = null) => new(
            LastAttemptAt: NowUtc,
            LastSuccessAt: lastSuccessAt,
            LastErrorCode: lastErrorCode,
            LastErrorSummary: null,
            ConsecutiveFailures: consecutiveFailures,
            LastDomainDate: CurrentTo,
            UpdatedAt: NowUtc);
}

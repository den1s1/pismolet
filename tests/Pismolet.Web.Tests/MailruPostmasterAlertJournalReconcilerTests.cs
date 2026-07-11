using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterAlertJournalReconcilerTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 11, 20, 0, 0, TimeSpan.Zero);
    private const string Domain = "pismolet.ru";

    [Fact]
    public void Reconcile_WhenAlertsDisabled_DoesNotChangeHistory()
    {
        var existing = ExistingEvent("spam_detected", "fingerprint-a");
        var result = Reconcile(
            Evaluation(Signal("spam_detected", "fingerprint-a")),
            [existing],
            alertsEnabled: false);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Reconcile_WhenEvaluationIsOff_DoesNotResolveExistingEvents()
    {
        var existing = ExistingEvent("spam_detected", "fingerprint-a");
        var result = Reconcile(
            new MailruPostmasterAlertEvaluation(
                MailruPostmasterAlertOverallStatus.Off,
                true,
                true,
                0,
                NowUtc,
                Array.Empty<MailruPostmasterAlertSignal>()),
            [existing]);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Reconcile_NewSignal_ActivatesJournalEvent()
    {
        var signal = Signal(
            "probably_spam_high",
            "fingerprint-new",
            severity: MailruPostmasterAlertSeverity.Warning,
            category: "probably_spam",
            messagesSent: 120,
            observedValue: 25,
            thresholdValue: 10);

        var result = Reconcile(Evaluation(signal));

        var change = Assert.Single(result.Activated);
        Assert.Equal(MailruPostmasterAlertJournalChangeType.Activated, change.ChangeType);
        Assert.Equal(Domain, change.Event.Domain);
        Assert.Equal("probably_spam_high", change.Event.Code);
        Assert.Equal("fingerprint-new", change.Event.Fingerprint);
        Assert.Equal(MailruPostmasterAlertEventStatus.Active, change.Event.Status);
        Assert.Equal(MailruPostmasterAlertSeverity.Warning, change.Event.Severity);
        Assert.Equal("probably_spam", change.Event.Category);
        Assert.Equal(120, change.Event.MessagesSent);
        Assert.Equal(25, change.Event.ObservedValue);
        Assert.Equal(10, change.Event.ThresholdValue);
        Assert.Equal(NowUtc, change.Event.FirstObservedAt);
        Assert.Equal(NowUtc, change.Event.LastObservedAt);
        Assert.Equal(1, change.Event.OccurrenceCount);
        Assert.Null(change.Event.ResolvedAt);
        Assert.Null(change.Event.LastNotifiedAt);
        Assert.NotEqual(Guid.Empty, change.Event.Id);
    }

    [Fact]
    public void Reconcile_MatchingSignal_UpdatesExistingEventAndPreservesIdentity()
    {
        var createdAt = NowUtc.AddDays(-2);
        var firstObservedAt = NowUtc.AddDays(-2);
        var lastNotifiedAt = NowUtc.AddHours(-12);
        var existing = ExistingEvent(
            "complaints_detected",
            "fingerprint-a",
            occurrenceCount: 4,
            createdAt: createdAt,
            firstObservedAt: firstObservedAt,
            lastNotifiedAt: lastNotifiedAt);
        var signal = Signal(
            "complaints_detected",
            "fingerprint-a",
            severity: MailruPostmasterAlertSeverity.Critical,
            category: "complaints",
            messagesSent: 1_000,
            observedValue: 5,
            thresholdValue: 0.3);

        var result = Reconcile(Evaluation(signal), [existing]);

        var change = Assert.Single(result.Updated);
        Assert.Equal(existing.Id, change.Event.Id);
        Assert.Equal(createdAt, change.Event.CreatedAt);
        Assert.Equal(firstObservedAt, change.Event.FirstObservedAt);
        Assert.Equal(lastNotifiedAt, change.Event.LastNotifiedAt);
        Assert.Equal(5, change.Event.OccurrenceCount);
        Assert.Equal(NowUtc, change.Event.LastObservedAt);
        Assert.Equal(NowUtc, change.Event.UpdatedAt);
        Assert.Equal(MailruPostmasterAlertSeverity.Critical, change.Event.Severity);
        Assert.Equal(1_000, change.Event.MessagesSent);
        Assert.Equal(5, change.Event.ObservedValue);
        Assert.Null(change.Event.ResolvedAt);
    }

    [Fact]
    public void Reconcile_DisappearedSignal_ResolvesExistingEvent()
    {
        var existing = ExistingEvent("sync_stale", "fingerprint-a");

        var result = Reconcile(Evaluation(), [existing]);

        var change = Assert.Single(result.Resolved);
        Assert.Equal(existing.Id, change.Event.Id);
        Assert.Equal(MailruPostmasterAlertEventStatus.Resolved, change.Event.Status);
        Assert.Equal(NowUtc, change.Event.ResolvedAt);
        Assert.Equal(NowUtc, change.Event.UpdatedAt);
        Assert.Equal(existing.LastObservedAt, change.Event.LastObservedAt);
        Assert.Equal(existing.OccurrenceCount, change.Event.OccurrenceCount);
    }

    [Fact]
    public void Reconcile_ChangedFingerprint_ActivatesNewEventAndResolvesPreviousOne()
    {
        var existing = ExistingEvent("probably_spam_high", "fingerprint-old");
        var signal = Signal("probably_spam_high", "fingerprint-new");

        var result = Reconcile(Evaluation(signal), [existing]);

        var activated = Assert.Single(result.Activated);
        var resolved = Assert.Single(result.Resolved);
        Assert.Equal("fingerprint-new", activated.Event.Fingerprint);
        Assert.Equal("fingerprint-old", resolved.Event.Fingerprint);
        Assert.NotEqual(existing.Id, activated.Event.Id);
        Assert.Equal(existing.Id, resolved.Event.Id);
    }

    [Fact]
    public void Reconcile_DuplicateSignals_ProducesSingleActivation()
    {
        var signal = Signal("spam_detected", "fingerprint-a");

        var result = Reconcile(Evaluation(signal, signal));

        Assert.Single(result.Activated);
        Assert.Empty(result.Updated);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public void Reconcile_IgnoresActiveEventsFromAnotherDomain()
    {
        var anotherDomainEvent = ExistingEvent(
            "spam_detected",
            "fingerprint-a",
            domain: "example.org");

        var result = Reconcile(Evaluation(), [anotherDomainEvent]);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Reconcile_IgnoresAlreadyResolvedEvents()
    {
        var resolved = ExistingEvent("spam_detected", "fingerprint-a") with
        {
            Status = MailruPostmasterAlertEventStatus.Resolved,
            ResolvedAt = NowUtc.AddDays(-1)
        };

        var result = Reconcile(Evaluation(), [resolved]);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Reconcile_NormalizesDomainCodeCategoryAndFingerprint()
    {
        var signal = Signal(
            "  SPAM_DETECTED  ",
            "  FINGERPRINT-A  ",
            category: "  SPAM  ");

        var result = new MailruPostmasterAlertJournalReconciler().Reconcile(
            "  PISMOLET.RU. ",
            Evaluation(signal),
            Array.Empty<MailruPostmasterAlertJournalEvent>(),
            NowUtc,
            alertsEnabled: true);

        var created = Assert.Single(result.Activated).Event;
        Assert.Equal("pismolet.ru", created.Domain);
        Assert.Equal("spam_detected", created.Code);
        Assert.Equal("spam", created.Category);
        Assert.Equal("fingerprint-a", created.Fingerprint);
    }

    [Fact]
    public void Reconcile_OccurrenceCount_DoesNotOverflow()
    {
        var existing = ExistingEvent(
            "spam_detected",
            "fingerprint-a",
            occurrenceCount: int.MaxValue);

        var result = Reconcile(
            Evaluation(Signal("spam_detected", "fingerprint-a")),
            [existing]);

        Assert.Equal(int.MaxValue, Assert.Single(result.Updated).Event.OccurrenceCount);
    }

    [Fact]
    public void Reconcile_OrdersChangesDeterministically()
    {
        var existing = ExistingEvent("z-old", "fingerprint-z");
        var result = Reconcile(
            Evaluation(
                Signal("b-new", "fingerprint-b"),
                Signal("a-new", "fingerprint-a")),
            [existing]);

        Assert.Equal(
            ["a-new", "b-new", "z-old"],
            result.Changes.Select(x => x.Event.Code).ToArray());
    }

    private static MailruPostmasterAlertJournalReconcileResult Reconcile(
        MailruPostmasterAlertEvaluation evaluation,
        IReadOnlyList<MailruPostmasterAlertJournalEvent>? existing = null,
        bool alertsEnabled = true) =>
        new MailruPostmasterAlertJournalReconciler().Reconcile(
            Domain,
            evaluation,
            existing ?? Array.Empty<MailruPostmasterAlertJournalEvent>(),
            NowUtc,
            alertsEnabled);

    private static MailruPostmasterAlertEvaluation Evaluation(
        params MailruPostmasterAlertSignal[] signals) => new(
        signals.Any(x => x.Severity == MailruPostmasterAlertSeverity.Critical)
            ? MailruPostmasterAlertOverallStatus.Critical
            : signals.Any(x => x.Severity == MailruPostmasterAlertSeverity.Warning)
                ? MailruPostmasterAlertOverallStatus.Attention
                : signals.Length > 0
                    ? MailruPostmasterAlertOverallStatus.NoData
                    : MailruPostmasterAlertOverallStatus.Calm,
        IsObservation: true,
        IsLowSample: false,
        MessagesSent: signals.Sum(x => x.MessagesSent),
        EvaluatedAt: NowUtc,
        Signals: signals);

    private static MailruPostmasterAlertSignal Signal(
        string code,
        string fingerprint,
        MailruPostmasterAlertSeverity severity = MailruPostmasterAlertSeverity.Warning,
        string category = "quality",
        long messagesSent = 100,
        double observedValue = 10,
        double thresholdValue = 5) => new(
        Code: code,
        Severity: severity,
        Category: category,
        Title: "Тестовый сигнал",
        Summary: "Тестовое описание",
        DateFrom: new DateOnly(2026, 7, 5),
        DateTo: new DateOnly(2026, 7, 11),
        MessagesSent: messagesSent,
        ObservedValue: observedValue,
        ThresholdValue: thresholdValue,
        IsLowSample: false,
        IsObservation: true,
        Source: "test",
        Fingerprint: fingerprint);

    private static MailruPostmasterAlertJournalEvent ExistingEvent(
        string code,
        string fingerprint,
        int occurrenceCount = 1,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? firstObservedAt = null,
        DateTimeOffset? lastNotifiedAt = null,
        string domain = Domain) => new(
        Id: Guid.NewGuid(),
        Domain: domain,
        Code: code,
        Severity: MailruPostmasterAlertSeverity.Warning,
        Category: "quality",
        Status: MailruPostmasterAlertEventStatus.Active,
        Fingerprint: fingerprint,
        DateFrom: new DateOnly(2026, 7, 1),
        DateTo: new DateOnly(2026, 7, 7),
        MessagesSent: 50,
        ObservedValue: 8,
        ThresholdValue: 5,
        FirstObservedAt: firstObservedAt ?? NowUtc.AddDays(-1),
        LastObservedAt: NowUtc.AddHours(-1),
        OccurrenceCount: occurrenceCount,
        ResolvedAt: null,
        LastNotifiedAt: lastNotifiedAt,
        CreatedAt: createdAt ?? NowUtc.AddDays(-1),
        UpdatedAt: NowUtc.AddHours(-1));
}

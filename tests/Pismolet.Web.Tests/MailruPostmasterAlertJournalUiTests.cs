using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterAlertJournalUiTests
{
    [Theory]
    [InlineData("active", "critical", MailruPostmasterAlertEventStatus.Active, MailruPostmasterAlertSeverity.Critical)]
    [InlineData("resolved", "warning", MailruPostmasterAlertEventStatus.Resolved, MailruPostmasterAlertSeverity.Warning)]
    [InlineData("unknown", "unknown", null, null)]
    [InlineData(null, null, null, null)]
    public void ParseFilter_RecognizesSupportedValuesAndFallsBackToAll(
        string? status,
        string? severity,
        MailruPostmasterAlertEventStatus? expectedStatus,
        MailruPostmasterAlertSeverity? expectedSeverity)
    {
        var filter = AdminMailruPostmasterAlertJournalRenderer.ParseFilter(status, severity);

        Assert.Equal(expectedStatus, filter.Status);
        Assert.Equal(expectedSeverity, filter.Severity);
    }

    [Fact]
    public void Render_ShowsFilteredJournalEventAndEncodesTechnicalValues()
    {
        var journalEvent = CreateEvent(
            code: "custom<script>",
            category: "category&test",
            status: MailruPostmasterAlertEventStatus.Resolved,
            severity: MailruPostmasterAlertSeverity.Critical);
        var filter = new AdminMailruPostmasterAlertJournalFilter(
            MailruPostmasterAlertEventStatus.Resolved,
            MailruPostmasterAlertSeverity.Critical);

        var html = AdminMailruPostmasterAlertJournalRenderer.Render(
            [journalEvent],
            filter,
            isAvailable: true);

        Assert.Contains("id='mailru-alert-journal'", html);
        Assert.Contains("Журнал сигналов", html);
        Assert.Contains("<option value='resolved' selected>", html);
        Assert.Contains("<option value='critical' selected>", html);
        Assert.Contains("Закрыт", html);
        Assert.Contains("Критично", html);
        Assert.Contains("custom&lt;script&gt;", html);
        Assert.Contains("category&amp;test", html);
        Assert.DoesNotContain("custom<script>", html);
        Assert.Contains("Срабатываний: 3", html);
        Assert.Contains("Писем: 250", html);
        Assert.Contains("Значение: 12,5", html);
        Assert.Contains("Порог: 10", html);
        Assert.Contains("05.07.2026 — 11.07.2026", html);
        Assert.Contains("Закрыт: 11.07.2026 19:00 UTC", html);
    }

    [Fact]
    public void Render_WhenEmptyExplainsThatSelectedFilterHasNoEvents()
    {
        var html = AdminMailruPostmasterAlertJournalRenderer.Render(
            Array.Empty<MailruPostmasterAlertJournalEvent>(),
            new AdminMailruPostmasterAlertJournalFilter(null, null),
            isAvailable: true);

        Assert.Contains("Показано: 0 из последних 100", html);
        Assert.Contains("Записей по выбранному фильтру нет", html);
        Assert.DoesNotContain("Журнал временно недоступен", html);
    }

    [Fact]
    public void Render_WhenUnavailableKeepsNonCriticalGuaranteeVisible()
    {
        var html = AdminMailruPostmasterAlertJournalRenderer.Render(
            Array.Empty<MailruPostmasterAlertJournalEvent>(),
            new AdminMailruPostmasterAlertJournalFilter(null, null),
            isAvailable: false);

        Assert.Contains("Журнал временно недоступен", html);
        Assert.Contains("не влияет на синхронизацию Mail.ru Postmaster и отправку писем", html);
    }

    [Fact]
    public void RenderAlertBlock_AppendsJournalWithoutChangingCurrentSignalSection()
    {
        const string journal = "<section id='mailru-alert-journal'>history</section>";
        var evaluation = new MailruPostmasterAlertEvaluation(
            MailruPostmasterAlertOverallStatus.Calm,
            IsObservation: true,
            IsLowSample: false,
            MessagesSent: 500,
            EvaluatedAt: DateTimeOffset.UtcNow,
            Signals: Array.Empty<MailruPostmasterAlertSignal>());

        var html = AdminMailruPostmasterAlertMiddleware.RenderAlertBlock(
            evaluation,
            new MailruPostmasterAlertOptions { Enabled = true, ObservationMode = true },
            new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 11),
            new DateOnly(2026, 6, 28),
            new DateOnly(2026, 7, 4),
            journal);

        Assert.Contains("Спокойно", html);
        Assert.Contains("эксплуатационные сигналы не сработали", html);
        Assert.Contains(journal, html);
    }

    private static MailruPostmasterAlertJournalEvent CreateEvent(
        string code,
        string category,
        MailruPostmasterAlertEventStatus status,
        MailruPostmasterAlertSeverity severity) => new(
        Id: Guid.Parse("52ba66b4-b30d-47a5-b6b8-2fd0d6501a84"),
        Domain: "pismolet.ru",
        Code: code,
        Severity: severity,
        Category: category,
        Status: status,
        Fingerprint: new string('a', 64),
        DateFrom: new DateOnly(2026, 7, 5),
        DateTo: new DateOnly(2026, 7, 11),
        MessagesSent: 250,
        ObservedValue: 12.5d,
        ThresholdValue: 10d,
        FirstObservedAt: new DateTimeOffset(2026, 7, 10, 18, 0, 0, TimeSpan.Zero),
        LastObservedAt: new DateTimeOffset(2026, 7, 11, 18, 0, 0, TimeSpan.Zero),
        OccurrenceCount: 3,
        ResolvedAt: new DateTimeOffset(2026, 7, 11, 19, 0, 0, TimeSpan.Zero),
        LastNotifiedAt: null,
        CreatedAt: new DateTimeOffset(2026, 7, 10, 18, 0, 0, TimeSpan.Zero),
        UpdatedAt: new DateTimeOffset(2026, 7, 11, 19, 0, 0, TimeSpan.Zero));
}

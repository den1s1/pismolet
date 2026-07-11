using Microsoft.Extensions.Configuration;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterAlertUiTests
{
    [Fact]
    public void OptionsReader_ReadsEnvironmentStyleKeysAndNormalizesValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailruPostmaster__AlertsEnabled"] = "true",
                ["MailruPostmaster__AlertsObservationMode"] = "false",
                ["MailruPostmaster__AlertsObservationStartedAt"] = "2026-07-11T18:00:00Z",
                ["MailruPostmaster__AlertsWindowDays"] = "99",
                ["MailruPostmaster__AlertsMinimumMessagesForRates"] = "250",
                ["MailruPostmaster__AlertsSpamCriticalCount"] = "2",
                ["MailruPostmaster__AlertsComplaintsWarningCount"] = "3",
                ["MailruPostmaster__AlertsComplaintsCriticalPercent"] = "0.5",
                ["MailruPostmaster__AlertsProbablySpamWarningPercent"] = "12.5",
                ["MailruPostmaster__AlertsProbablySpamGrowthPoints"] = "7.5",
                ["MailruPostmaster__AlertsSyncStaleHours"] = "999",
                ["MailruPostmaster__AlertsConsecutiveFailures"] = "4",
                ["MailruPostmaster__AlertsNotificationCooldownHours"] = "12",
                ["MailruPostmaster__AlertsNotificationsEnabled"] = "true"
            })
            .Build();

        var options = MailruPostmasterAlertOptionsReader.Read(configuration);

        Assert.True(options.Enabled);
        Assert.False(options.ObservationMode);
        Assert.Equal(new DateTimeOffset(2026, 7, 11, 18, 0, 0, TimeSpan.Zero), options.ObservationStartedAt);
        Assert.Equal(30, options.WindowDays);
        Assert.Equal(250, options.MinimumMessagesForRates);
        Assert.Equal(2, options.SpamCriticalCount);
        Assert.Equal(3, options.ComplaintsWarningCount);
        Assert.Equal(0.5d, options.ComplaintsCriticalPercent, precision: 6);
        Assert.Equal(12.5d, options.ProbablySpamWarningPercent, precision: 6);
        Assert.Equal(7.5d, options.ProbablySpamGrowthPoints, precision: 6);
        Assert.Equal(168, options.SyncStaleHours);
        Assert.Equal(4, options.ConsecutiveFailures);
        Assert.Equal(12, options.NotificationCooldownHours);
        Assert.True(options.NotificationsEnabled);
    }

    [Fact]
    public void RenderAlertBlock_ShowsObservationLowSampleAndEncodesDynamicText()
    {
        var signal = new MailruPostmasterAlertSignal(
            Code: "danger<script>",
            Severity: MailruPostmasterAlertSeverity.Critical,
            Category: "spam",
            Title: "Опасность <script>alert(1)</script>",
            Summary: "Проверить A & B",
            DateFrom: new DateOnly(2026, 7, 5),
            DateTo: new DateOnly(2026, 7, 11),
            MessagesSent: 12,
            ObservedValue: 1,
            ThresholdValue: 1,
            IsLowSample: true,
            IsObservation: true,
            Source: "domain_daily_metrics",
            Fingerprint: new string('a', 64));
        var evaluation = new MailruPostmasterAlertEvaluation(
            MailruPostmasterAlertOverallStatus.Critical,
            IsObservation: true,
            IsLowSample: true,
            MessagesSent: 12,
            EvaluatedAt: new DateTimeOffset(2026, 7, 11, 18, 0, 0, TimeSpan.Zero),
            Signals: [signal]);
        var options = new MailruPostmasterAlertOptions
        {
            Enabled = true,
            ObservationMode = true,
            ObservationStartedAt = new DateTimeOffset(2026, 7, 11, 18, 0, 0, TimeSpan.Zero),
            MinimumMessagesForRates = 100
        };

        var html = AdminMailruPostmasterAlertMiddleware.RenderAlertBlock(
            evaluation,
            options,
            new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 11),
            new DateOnly(2026, 6, 28),
            new DateOnly(2026, 7, 4));

        Assert.Contains("id='mailru-operational-alerts'", html);
        Assert.Contains("Критично", html);
        Assert.Contains("Режим наблюдения", html);
        Assert.Contains("Малая выборка", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("A &amp; B", html);
        Assert.Contains("danger&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("05.07.2026 — 11.07.2026", html);
    }

    [Fact]
    public void RenderAlertBlock_WhenCalmExplainsThatNoSignalsTriggered()
    {
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
            new DateOnly(2026, 7, 4));

        Assert.Contains("Спокойно", html);
        Assert.Contains("эксплуатационные сигналы не сработали", html);
        Assert.DoesNotContain("Малая выборка", html);
    }

    [Fact]
    public void RenderAlertBlock_WhenOffExplainsThatMetricsContinueIndependently()
    {
        var evaluation = new MailruPostmasterAlertEvaluation(
            MailruPostmasterAlertOverallStatus.Off,
            IsObservation: true,
            IsLowSample: true,
            MessagesSent: 0,
            EvaluatedAt: DateTimeOffset.UtcNow,
            Signals: Array.Empty<MailruPostmasterAlertSignal>());

        var html = AdminMailruPostmasterAlertMiddleware.RenderAlertBlock(
            evaluation,
            new MailruPostmasterAlertOptions(),
            new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 11),
            new DateOnly(2026, 6, 28),
            new DateOnly(2026, 7, 4));

        Assert.Contains("Выключено", html);
        Assert.Contains("Сбор доменных метрик и статистики рассылок продолжает работать независимо", html);
    }

    [Fact]
    public void InjectAlertBlock_InsertsBeforeSummaryAndDoesNotDuplicate()
    {
        const string page = "<main><div class='mailru-summary'>summary</div></main>";
        const string block = "<section id='mailru-operational-alerts'>alerts</section>";

        var first = AdminMailruPostmasterAlertMiddleware.InjectAlertBlock(page, block);
        var second = AdminMailruPostmasterAlertMiddleware.InjectAlertBlock(first, block);

        Assert.Contains(block + "<div class='mailru-summary'>", first);
        Assert.Equal(first, second);
        Assert.Equal(1, CountOccurrences(second, "id='mailru-operational-alerts'"));
    }

    [Fact]
    public void RenderUnavailableBlock_DoesNotClaimThatSendingIsAffected()
    {
        var html = AdminMailruPostmasterAlertMiddleware.RenderUnavailableBlock();

        Assert.Contains("Сигналы временно недоступны", html);
        Assert.Contains("не влияет на отправку писем", html);
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }
}

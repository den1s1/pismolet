using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterMailingMetricsReaderTests
{
    [Fact]
    public async Task Reader_ReturnsOnlyRequestedDomainAndMailing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var mailingId = Guid.NewGuid();
        var otherMailingId = Guid.NewGuid();
        var observedAt = new DateTimeOffset(2026, 7, 10, 16, 30, 0, TimeSpan.Zero);
        var msgType = MailruPostmasterMessageType.Build(mailingId);

        db.MailingDailyMetrics.AddRange(
            Metric(mailingId, "pismolet.ru", msgType, new DateOnly(2026, 7, 9), 100, 90, observedAt),
            Metric(mailingId, "pismolet.ru", msgType, new DateOnly(2026, 7, 8), 50, 40, observedAt),
            Metric(otherMailingId, "pismolet.ru", MailruPostmasterMessageType.Build(otherMailingId), new DateOnly(2026, 7, 9), 300, 250, observedAt),
            Metric(mailingId, "other.example", msgType, new DateOnly(2026, 7, 9), 400, 350, observedAt));
        db.MailingSyncStates.Add(new MailruPostmasterMailingSyncStateEntity
        {
            Domain = "pismolet.ru",
            MailingId = mailingId,
            MsgType = msgType,
            FirstSendAt = observedAt.AddDays(-2),
            LastSendAt = observedAt.AddDays(-1),
            LastAttemptAt = observedAt,
            LastSuccessAt = observedAt,
            LastDateTo = new DateOnly(2026, 7, 9),
            HasData = true,
            NextAttemptAt = observedAt.AddDays(1),
            UpdatedAt = observedAt
        });
        await db.SaveChangesAsync();

        var reader = new EfMailruPostmasterMailingMetricsReader(db);
        var result = await reader.ReadAsync("PISMOLET.RU.", mailingId);

        Assert.Equal("pismolet.ru", result.Domain);
        Assert.Equal(mailingId, result.MailingId);
        Assert.Equal(msgType, result.MsgType);
        Assert.NotNull(result.State);
        Assert.Equal(observedAt, result.State.LastSuccessAt);
        Assert.Equal(2, result.Days.Count);
        Assert.Equal(new DateOnly(2026, 7, 8), result.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 9), result.Days[1].Date);
        Assert.Equal(150, result.Days.Sum(x => x.MessagesSent));
    }

    [Fact]
    public void Summarize_UsesAbsoluteTotalsForPercentages()
    {
        var days = new[]
        {
            Day(new DateOnly(2026, 7, 8), sent: 100, delivered: 50, probablySpam: 40, spam: 5, complaints: 1),
            Day(new DateOnly(2026, 7, 9), sent: 900, delivered: 850, probablySpam: 90, spam: 9, complaints: 3)
        };

        var summary = MailruPostmasterMailingMetricsCalculator.Summarize(days);

        Assert.Equal(2, summary.DataDays);
        Assert.Equal(new DateOnly(2026, 7, 8), summary.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 9), summary.DateTo);
        Assert.Equal(1000, summary.MessagesSent);
        Assert.Equal(900, summary.Delivered);
        Assert.Equal(130, summary.ProbablySpam);
        Assert.Equal(14, summary.Spam);
        Assert.Equal(4, summary.Complaints);
        Assert.Equal(90d, summary.DeliveredPercent, precision: 6);
        Assert.Equal(13d, summary.ProbablySpamPercent, precision: 6);
        Assert.Equal(1.4d, summary.SpamPercent, precision: 6);
        Assert.Equal(0.4d, summary.ComplaintsPercent, precision: 6);
    }

    [Fact]
    public void ResolveStatus_CoversDisabledEmptyUpdatingStableCompletedAndErrorStates()
    {
        var mailingId = Guid.NewGuid();
        var empty = MailruPostmasterMailingMetricsData.Empty("pismolet.ru", mailingId);
        var now = new DateTimeOffset(2026, 7, 10, 16, 30, 0, TimeSpan.Zero);

        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.Disabled,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(false, true, empty));
        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.Disabled,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(true, false, empty));
        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.WaitingForFirstCompletedDay,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(true, true, empty));
        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.NoData,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(
                true,
                true,
                WithState(empty, State(now, lastSuccessAt: now))));
        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.Updating,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(
                true,
                true,
                WithState(empty, State(now, lastSuccessAt: now, hasData: true))));
        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.Stable,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(
                true,
                true,
                WithState(empty, State(now, lastSuccessAt: now, hasData: true, isStable: true))));
        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.Completed,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(
                true,
                true,
                WithState(empty, State(now, lastSuccessAt: now, hasData: true, completedAt: now))));
        Assert.Equal(
            MailruPostmasterMailingMetricsViewStatus.Failed,
            MailruPostmasterMailingMetricsCalculator.ResolveStatus(
                true,
                true,
                WithState(empty, State(now, errorCode: "api_error"))));
    }

    [Fact]
    public void Block_SeparatesMailruMetricsAndShowsLocalState()
    {
        var mailingId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 10, 16, 30, 0, TimeSpan.Zero);
        var data = new MailruPostmasterMailingMetricsData(
            "pismolet.ru",
            mailingId,
            MailruPostmasterMessageType.Build(mailingId),
            State(now, lastSuccessAt: now, hasData: true),
            [Day(new DateOnly(2026, 7, 9), 100, 80, 15, 3, 2)]);

        var html = AdminCampaignMailruPostmasterMiddleware.BuildBlock(
            data,
            integrationEnabled: true,
            mailingMetricsEnabled: true);

        Assert.Contains("Mail.ru Postmaster", html);
        Assert.Contains("Данные относятся только к экосистеме Mail.ru", html);
        Assert.Contains("отдельно от внутренних показателей Письмолёта", html);
        Assert.Contains(data.MsgType, html);
        Assert.Contains("Доставлено · 80%", html);
        Assert.Contains("Данные обновляются", html);
        Assert.DoesNotContain("email", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockInjection_IsIdempotent()
    {
        var mailingId = Guid.NewGuid();
        var data = MailruPostmasterMailingMetricsData.Empty("pismolet.ru", mailingId);
        const string page = "<main><div class='section-head'><div><p class='eyebrow'>Отправка</p><h2>Лог отправки</h2></div></main>";

        var first = AdminCampaignMailruPostmasterMiddleware.InjectBlock(page, data, false, false);
        var second = AdminCampaignMailruPostmasterMiddleware.InjectBlock(first, data, false, false);

        Assert.Equal(first, second);
        Assert.Equal(1, CountOccurrences(second, "data-mailru-postmaster-block='true'"));
        Assert.Contains("PM-4 отключён", second);
    }

    [Fact]
    public async Task EmptyReader_ReturnsCanonicalMsgTypeWithoutExternalDependencies()
    {
        var mailingId = Guid.NewGuid();
        var reader = new EmptyMailruPostmasterMailingMetricsReader();

        var result = await reader.ReadAsync("PISMOLET.RU.", mailingId);

        Assert.Equal("pismolet.ru", result.Domain);
        Assert.Equal(MailruPostmasterMessageType.Build(mailingId), result.MsgType);
        Assert.Null(result.State);
        Assert.Empty(result.Days);
    }

    private static async Task<MailruPostmasterDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<MailruPostmasterDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new MailruPostmasterDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static MailruPostmasterMailingDailyMetricEntity Metric(
        Guid mailingId,
        string domain,
        string msgType,
        DateOnly date,
        long sent,
        long delivered,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            MailingId = mailingId,
            Domain = domain,
            MsgType = msgType,
            Date = date,
            MessagesSent = sent,
            Delivered = delivered,
            ProbablySpam = Math.Max(0, sent - delivered),
            Read = delivered / 2,
            CollectedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private static MailruPostmasterMailingMetricsDay Day(
        DateOnly date,
        long sent,
        long delivered,
        long probablySpam,
        long spam,
        long complaints) =>
        new(
            Date: date,
            MessagesSent: sent,
            Delivered: delivered,
            ProbablySpam: probablySpam,
            Spam: spam,
            Complaints: complaints,
            Read: delivered / 2,
            DeletedRead: 2,
            DeletedUnread: 4,
            SpamPercent: MailruPostmasterMailingMetricsCalculator.Percent(spam, sent),
            ProbablySpamPercent: MailruPostmasterMailingMetricsCalculator.Percent(probablySpam, sent),
            Reputation: 0.2d,
            Trend: 0.1d,
            UpdatedAt: new DateTimeOffset(2026, 7, 10, 16, 30, 0, TimeSpan.Zero));

    private static MailruPostmasterMailingMetricsState State(
        DateTimeOffset now,
        DateTimeOffset? lastSuccessAt = null,
        bool hasData = false,
        bool isStable = false,
        DateTimeOffset? completedAt = null,
        string? errorCode = null) =>
        new(
            FirstSendAt: now.AddDays(-2),
            LastSendAt: now.AddDays(-1),
            LastAttemptAt: now,
            LastSuccessAt: lastSuccessAt,
            LastErrorCode: errorCode,
            LastErrorSummary: errorCode is null ? null : "Безопасное описание ошибки.",
            ConsecutiveFailures: errorCode is null ? 0 : 1,
            LastDateTo: new DateOnly(2026, 7, 9),
            NextAttemptAt: completedAt is null ? now.AddDays(1) : null,
            HasData: hasData,
            IsStable: isStable,
            CompletedAt: completedAt,
            UpdatedAt: now);

    private static MailruPostmasterMailingMetricsData WithState(
        MailruPostmasterMailingMetricsData data,
        MailruPostmasterMailingMetricsState state) =>
        data with { State = state };

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

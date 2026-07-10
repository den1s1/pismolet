using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed record MailruPostmasterMailingMetricsOptions(
    bool Enabled,
    DateOnly FromDate,
    int BatchSize,
    int ResyncDays,
    int StableRuns,
    int MaxAgeDays)
{
    public const int MinBatchSize = 1;
    public const int MaxBatchSize = 100;
    public const int MinResyncDays = 1;
    public const int MaxResyncDays = 14;
    public const int MinStableRuns = 1;
    public const int MaxStableRuns = 10;
    public const int MinMaxAgeDays = 7;
    public const int MaxMaxAgeDays = 90;

    public static MailruPostmasterMailingMetricsOptions Default => new(
        Enabled: false,
        FromDate: new DateOnly(2026, 7, 9),
        BatchSize: 20,
        ResyncDays: 3,
        StableRuns: 2,
        MaxAgeDays: 30);

    public static MailruPostmasterMailingMetricsOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var fallback = Default;
        return new MailruPostmasterMailingMetricsOptions(
            Enabled: ReadBool(configuration, "MailruPostmaster:MailingMetricsEnabled", fallback.Enabled),
            FromDate: ReadDate(configuration, "MailruPostmaster:MailingMetricsFromDate", fallback.FromDate),
            BatchSize: ReadInt(configuration, "MailruPostmaster:MailingMetricsBatchSize", fallback.BatchSize, MinBatchSize, MaxBatchSize),
            ResyncDays: ReadInt(configuration, "MailruPostmaster:MailingMetricsResyncDays", fallback.ResyncDays, MinResyncDays, MaxResyncDays),
            StableRuns: ReadInt(configuration, "MailruPostmaster:MailingMetricsStableRuns", fallback.StableRuns, MinStableRuns, MaxStableRuns),
            MaxAgeDays: ReadInt(configuration, "MailruPostmaster:MailingMetricsMaxAgeDays", fallback.MaxAgeDays, MinMaxAgeDays, MaxMaxAgeDays));
    }

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        var value = ReadValue(configuration, key);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static DateOnly ReadDate(IConfiguration configuration, string key, DateOnly fallback)
    {
        var value = ReadValue(configuration, key);
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
    }

    private static int ReadInt(
        IConfiguration configuration,
        string key,
        int fallback,
        int min,
        int max)
    {
        var value = ReadValue(configuration, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static string? ReadValue(IConfiguration configuration, string key)
    {
        var environmentKey = key.Replace(":", "__", StringComparison.Ordinal);
        return configuration[key]
            ?? configuration[environmentKey]
            ?? Environment.GetEnvironmentVariable(environmentKey);
    }
}

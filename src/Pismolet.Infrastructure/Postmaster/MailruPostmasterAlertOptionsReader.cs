using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Pismolet.Web.Infrastructure.Postmaster;

public static class MailruPostmasterAlertOptionsReader
{
    public static MailruPostmasterAlertOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var fallback = new MailruPostmasterAlertOptions();
        return new MailruPostmasterAlertOptions
        {
            Enabled = ReadBool(configuration, "MailruPostmaster:AlertsEnabled", fallback.Enabled),
            ObservationMode = ReadBool(configuration, "MailruPostmaster:AlertsObservationMode", fallback.ObservationMode),
            ObservationStartedAt = ReadDateTimeOffset(configuration, "MailruPostmaster:AlertsObservationStartedAt"),
            WindowDays = ReadInt(configuration, "MailruPostmaster:AlertsWindowDays", fallback.WindowDays),
            MinimumMessagesForRates = ReadLong(configuration, "MailruPostmaster:AlertsMinimumMessagesForRates", fallback.MinimumMessagesForRates),
            SpamCriticalCount = ReadLong(configuration, "MailruPostmaster:AlertsSpamCriticalCount", fallback.SpamCriticalCount),
            ComplaintsWarningCount = ReadLong(configuration, "MailruPostmaster:AlertsComplaintsWarningCount", fallback.ComplaintsWarningCount),
            ComplaintsCriticalPercent = ReadDouble(configuration, "MailruPostmaster:AlertsComplaintsCriticalPercent", fallback.ComplaintsCriticalPercent),
            ProbablySpamWarningPercent = ReadDouble(configuration, "MailruPostmaster:AlertsProbablySpamWarningPercent", fallback.ProbablySpamWarningPercent),
            ProbablySpamGrowthPoints = ReadDouble(configuration, "MailruPostmaster:AlertsProbablySpamGrowthPoints", fallback.ProbablySpamGrowthPoints),
            SyncStaleHours = ReadInt(configuration, "MailruPostmaster:AlertsSyncStaleHours", fallback.SyncStaleHours),
            ConsecutiveFailures = ReadInt(configuration, "MailruPostmaster:AlertsConsecutiveFailures", fallback.ConsecutiveFailures),
            NotificationCooldownHours = ReadInt(configuration, "MailruPostmaster:AlertsNotificationCooldownHours", fallback.NotificationCooldownHours),
            NotificationsEnabled = ReadBool(configuration, "MailruPostmaster:AlertsNotificationsEnabled", fallback.NotificationsEnabled)
        }.Normalize();
    }

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        var value = ReadValue(configuration, key);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback)
    {
        var value = ReadValue(configuration, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static long ReadLong(IConfiguration configuration, string key, long fallback)
    {
        var value = ReadValue(configuration, key);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static double ReadDouble(IConfiguration configuration, string key, double fallback)
    {
        var value = ReadValue(configuration, key);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static DateTimeOffset? ReadDateTimeOffset(IConfiguration configuration, string key)
    {
        var value = ReadValue(configuration, key);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadValue(IConfiguration configuration, string key)
    {
        var environmentKey = key.Replace(":", "__", StringComparison.Ordinal);
        return configuration[key]
            ?? configuration[environmentKey]
            ?? Environment.GetEnvironmentVariable(environmentKey);
    }
}

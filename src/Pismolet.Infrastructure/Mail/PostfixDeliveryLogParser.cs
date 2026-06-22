using System.Globalization;
using System.Text.RegularExpressions;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public enum PostfixDeliveryLogStatus
{
    Sent,
    Deferred,
    Bounced,
    Expired,
    Unknown
}

public sealed record PostfixDeliveryLogEvent(
    string QueueId,
    string RecipientEmail,
    PostfixDeliveryLogStatus Status,
    DeliveryStatus DeliveryStatus,
    string? Dsn,
    string? Relay,
    string? Diagnostic,
    DateTimeOffset OccurredAt);

public static partial class PostfixDeliveryLogParser
{
    private static readonly string[] MonthNames =
    [
        "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    ];

    public static bool TryParse(string line, int year, TimeSpan utcOffset, out PostfixDeliveryLogEvent? deliveryEvent)
    {
        deliveryEvent = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = PostfixSmtpLineRegex().Match(line.Trim());
        if (!match.Success)
        {
            return false;
        }

        var month = Array.IndexOf(MonthNames, match.Groups["month"].Value) + 1;
        if (month <= 0)
        {
            return false;
        }

        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture);
        var occurredAt = new DateTimeOffset(year, month, day, hour, minute, second, utcOffset).ToUniversalTime();

        var statusText = match.Groups["status"].Value.Trim().ToLowerInvariant();
        var status = statusText switch
        {
            "sent" => PostfixDeliveryLogStatus.Sent,
            "deferred" => PostfixDeliveryLogStatus.Deferred,
            "bounced" => PostfixDeliveryLogStatus.Bounced,
            "expired" => PostfixDeliveryLogStatus.Expired,
            _ => PostfixDeliveryLogStatus.Unknown
        };

        var deliveryStatus = status switch
        {
            PostfixDeliveryLogStatus.Sent => DeliveryStatus.Delivered,
            PostfixDeliveryLogStatus.Deferred => DeliveryStatus.SoftBounce,
            PostfixDeliveryLogStatus.Bounced => DeliveryStatus.HardBounce,
            PostfixDeliveryLogStatus.Expired => DeliveryStatus.HardBounce,
            _ => DeliveryStatus.Unknown
        };

        deliveryEvent = new PostfixDeliveryLogEvent(
            match.Groups["queueId"].Value.Trim(),
            match.Groups["recipient"].Value.Trim().ToLowerInvariant(),
            status,
            deliveryStatus,
            EmptyToNull(match.Groups["dsn"].Value),
            EmptyToNull(match.Groups["relay"].Value),
            EmptyToNull(match.Groups["diagnostic"].Value),
            occurredAt);
        return true;
    }

    private static string? EmptyToNull(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    [GeneratedRegex(@"^(?<month>[A-Z][a-z]{2})\s+(?<day>\d{1,2})\s+(?<hour>\d{2}):(?<minute>\d{2}):(?<second>\d{2})\s+\S+\s+postfix/smtp\[\d+\]:\s+(?<queueId>[A-F0-9]+):\s+to=<(?<recipient>[^>]+)>,\s+relay=(?<relay>.*?),\s+delay=[^,]+,\s+delays=[^,]+,\s+dsn=(?<dsn>[^,]+),\s+status=(?<status>[a-zA-Z]+)\s+\((?<diagnostic>.*)\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex PostfixSmtpLineRegex();
}

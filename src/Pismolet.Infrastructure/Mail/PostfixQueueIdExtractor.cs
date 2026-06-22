using System.Text.RegularExpressions;

namespace Pismolet.Web.Infrastructure.Mail;

public static partial class PostfixQueueIdExtractor
{
    public static string? TryExtract(string? smtpResponse)
    {
        if (string.IsNullOrWhiteSpace(smtpResponse))
        {
            return null;
        }

        var match = QueuedAsRegex().Match(smtpResponse);
        return match.Success
            ? match.Groups["queueId"].Value.Trim().ToUpperInvariant()
            : null;
    }

    [GeneratedRegex(@"\bqueued\s+as\s+(?<queueId>[A-F0-9]{5,32})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex QueuedAsRegex();
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class RecipientImportIssueStore
{
    private static readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "pismolet-recipient-issues");
    private static readonly Regex IssueRowRegex = new(
        @"<li>\s*<b>Строка\s+(?<row>\d+)</b>\s*<span>(?<email>.*?)</span>\s*<em>(?<message>.*?)</em>\s*</li>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static void Save(Mailing mailing)
    {
        var issues = mailing.LastImportBatch?.Issues.ToArray() ?? Array.Empty<RecipientImportIssue>();
        Save(mailing.Id, issues.Select(issue => new RecipientImportIssueSnapshot(issue.RowNumber, issue.Email, issue.Message)));
    }

    public static void Save(Guid mailingId, IEnumerable<RecipientImportIssueSnapshot> issues)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = PathFor(mailingId);
        var lines = issues.Select(issue => string.Join('\t', issue.RowNumber.ToString(CultureInfo.InvariantCulture), Escape(issue.Email), Escape(issue.Message)));
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    public static void SaveFromHtml(Guid mailingId, string html)
    {
        var issues = IssueRowRegex.Matches(html)
            .Select(match => ToIssue(match))
            .Where(issue => issue is not null)
            .Cast<RecipientImportIssueSnapshot>()
            .ToArray();

        if (issues.Length > 0)
        {
            Save(mailingId, issues);
        }
    }

    public static IReadOnlyList<RecipientImportIssueSnapshot> Load(Guid mailingId)
    {
        var path = PathFor(mailingId);
        if (!File.Exists(path))
        {
            return Array.Empty<RecipientImportIssueSnapshot>();
        }

        return File.ReadAllLines(path, Encoding.UTF8)
            .Select(Parse)
            .Where(issue => issue is not null)
            .Cast<RecipientImportIssueSnapshot>()
            .ToArray();
    }

    private static RecipientImportIssueSnapshot? ToIssue(Match match)
    {
        return int.TryParse(match.Groups["row"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row)
            ? new RecipientImportIssueSnapshot(row, Decode(match.Groups["email"].Value), Decode(match.Groups["message"].Value))
            : null;
    }

    private static RecipientImportIssueSnapshot? Parse(string line)
    {
        var parts = line.Split('\t');
        return parts.Length >= 3 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row)
            ? new RecipientImportIssueSnapshot(row, Unescape(parts[1]), Unescape(parts[2]))
            : null;
    }

    private static string Decode(string value) => WebUtility.HtmlDecode(Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline)).Trim();
    private static string PathFor(Guid mailingId) => Path.Combine(DirectoryPath, mailingId + ".tsv");
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);
    private static string Unescape(string value) => value.Replace("\\r", "\r", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\t", "\t", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
}

public sealed record RecipientImportIssueSnapshot(int RowNumber, string Email, string Message);

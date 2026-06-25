using System.Globalization;
using System.Text;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class RecipientImportIssueStore
{
    private static readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "pismolet-recipient-issues");

    public static void Save(Mailing mailing)
    {
        var issues = mailing.LastImportBatch?.Issues.ToArray() ?? Array.Empty<RecipientImportIssue>();
        Save(mailing.Id, issues.Select(issue => new RecipientImportIssueSnapshot(issue.RowNumber, issue.Email, issue.Message)));
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

    private static void Save(Guid mailingId, IEnumerable<RecipientImportIssueSnapshot> issues)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = PathFor(mailingId);
        var lines = issues.Select(issue => string.Join('\t', issue.RowNumber.ToString(CultureInfo.InvariantCulture), Escape(issue.Email), Escape(issue.Message)));
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static RecipientImportIssueSnapshot? Parse(string line)
    {
        var parts = line.Split('\t');
        return parts.Length >= 3 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row)
            ? new RecipientImportIssueSnapshot(row, Unescape(parts[1]), Unescape(parts[2]))
            : null;
    }

    private static string PathFor(Guid mailingId) => Path.Combine(DirectoryPath, mailingId + ".tsv");
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);
    private static string Unescape(string value) => value.Replace("\\r", "\r", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\t", "\t", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
}

public sealed record RecipientImportIssueSnapshot(int RowNumber, string Email, string Message);

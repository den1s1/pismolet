namespace Pismolet.Web.Domain.Mailings;

public sealed record ImportStats(
    int TotalRows,
    int Accepted,
    int Duplicates,
    int Invalid,
    int GloballySuppressed)
{
    public static ImportStats Empty { get; } = new(0, 0, 0, 0, 0);
}

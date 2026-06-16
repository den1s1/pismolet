namespace Pismolet.Web.Application.Imports;

public interface IEmailNormalizer
{
    string Normalize(string? value);
}

public sealed class EmailNormalizer : IEmailNormalizer
{
    public string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().ToLowerInvariant();
}

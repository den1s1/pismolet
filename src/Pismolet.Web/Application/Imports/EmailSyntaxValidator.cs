namespace Pismolet.Web.Application.Imports;

public interface IEmailSyntaxValidator
{
    bool IsValid(string normalizedEmail);
}

public sealed class EmailSyntaxValidator : IEmailSyntaxValidator
{
    public bool IsValid(string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail) || normalizedEmail.Length > 254)
        {
            return false;
        }

        if (normalizedEmail.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var parts = normalizedEmail.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        return parts[1].Contains('.', StringComparison.Ordinal) && !parts[1].StartsWith('.') && !parts[1].EndsWith('.');
    }
}

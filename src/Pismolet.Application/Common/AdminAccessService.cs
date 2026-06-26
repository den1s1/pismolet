namespace Pismolet.Web.Application.Common;

public interface IAdminAccessService
{
    bool IsAdminEmail(string? email);

    bool IsConfigAdminEmail(string? email);

    bool IsManagedAdminEmail(string? email);

    IReadOnlyCollection<string> ListManagedAdminEmails();

    void GrantAdmin(string email, string grantedByEmail);

    bool TryRevokeAdmin(string email, string revokedByEmail, out string error);
}

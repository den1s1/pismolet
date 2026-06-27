using Pismolet.Web.Application.Common;

namespace Pismolet.Web.Application.Admin;

public sealed record AdminUserRemovalResult(bool Ok, string Error, int MailingsRemoved)
{
    public static AdminUserRemovalResult Success(int mailingsRemoved) => new(true, string.Empty, mailingsRemoved);

    public static AdminUserRemovalResult Failure(string error) => new(false, error, 0);
}

public interface IAdminUserRemovalService
{
    AdminUserRemovalResult RemoveUser(string targetEmail, string adminEmail, RequestMetadata request);
}

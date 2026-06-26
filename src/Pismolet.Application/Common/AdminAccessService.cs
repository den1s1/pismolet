namespace Pismolet.Web.Application.Common;

public interface IAdminAccessService
{
    bool IsAdminEmail(string? email);
}

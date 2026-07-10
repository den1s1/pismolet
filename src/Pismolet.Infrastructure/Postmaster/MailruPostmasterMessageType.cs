namespace Pismolet.Web.Infrastructure.Postmaster;

public static class MailruPostmasterMessageType
{
    public static string Build(Guid mailingId) => mailingId.ToString("N");
}

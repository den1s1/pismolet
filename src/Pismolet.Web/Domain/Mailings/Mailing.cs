namespace Pismolet.Web.Domain.Mailings;

public sealed record Mailing(string Subject, string StatusRu)
{
    public static Mailing Draft(string subject) => new(subject, "Черновик");
}

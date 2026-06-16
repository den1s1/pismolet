namespace Pismolet.Web.Domain.Audit;

public sealed record AuditRecord(
    DateTimeOffset CreatedAt,
    string User,
    string EventType,
    string Ip,
    string UserAgent,
    string Context);

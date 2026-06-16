using Pismolet.Web.Domain.Audit;

namespace Pismolet.Web.Application.Audit;

public interface IAuditLogger
{
    void Write(AuditRecord record);

    IReadOnlyCollection<AuditRecord> GetRecords();
}

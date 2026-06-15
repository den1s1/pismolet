using System.Collections.Concurrent;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Domain.Audit;

namespace Pismolet.Web.Infrastructure.Audit;

public sealed class InMemoryAuditLogger : IAuditLogger
{
    private readonly ConcurrentQueue<AuditRecord> _records = new();

    public void Write(AuditRecord record) => _records.Enqueue(record);

    public IReadOnlyCollection<AuditRecord> GetRecords() => _records.Reverse().ToArray();
}

using System.Collections.Concurrent;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Audit;

public sealed class InMemoryAuditLogger : IAuditLogger
{
    private readonly ConcurrentQueue<AuditRecord> _records = new();

    public void Write(AuditRecord record) => _records.Enqueue(record);

    public IReadOnlyCollection<AuditRecord> GetRecords() => _records.Reverse().ToArray();
}

public sealed class EfAuditLogger(PismoletDbContext db) : IAuditLogger
{
    public void Write(AuditRecord record)
    {
        db.AuditRecords.Add(new AuditRecordEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = record.CreatedAt.ToUniversalTime(),
            User = record.User,
            EventType = record.EventType,
            Ip = record.Ip,
            UserAgent = record.UserAgent,
            Context = record.Context
        });
        db.SaveChanges();
    }

    public IReadOnlyCollection<AuditRecord> GetRecords() => db.AuditRecords
        .OrderByDescending(x => x.CreatedAt)
        .Select(x => new AuditRecord(x.CreatedAt, x.User, x.EventType, x.Ip, x.UserAgent, x.Context))
        .ToArray();
}

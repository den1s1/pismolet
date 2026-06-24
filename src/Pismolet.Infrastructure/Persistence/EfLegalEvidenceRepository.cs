using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfLegalEvidenceRepository(PismoletDbContext db) : ILegalEvidenceRepository
{
    public void SaveDocumentVersion(LegalDocumentVersion version)
    {
        var key = Normalize(version.DocumentKey);
        var versionCode = Normalize(version.Version);
        if (version.IsActive)
        {
            foreach (var active in db.LegalDocumentVersions.Where(x => x.DocumentKey == key && x.IsActive).ToArray())
            {
                active.IsActive = false;
            }
        }

        var existing = db.LegalDocumentVersions.FirstOrDefault(x => x.DocumentKey == key && x.Version == versionCode);
        if (existing is null)
        {
            db.LegalDocumentVersions.Add(ToDocumentEntity(version with { DocumentKey = key, Version = versionCode }));
        }
        else
        {
            existing.TextHash = version.TextHash;
            existing.Text = version.Text;
            existing.Url = version.Url;
            existing.IsActive = version.IsActive;
            existing.CreatedAt = version.CreatedAt.ToUniversalTime();
        }

        db.SaveChanges();
    }

    public LegalDocumentVersion? GetDocumentVersion(string documentKey, string version)
    {
        var key = Normalize(documentKey);
        var versionCode = Normalize(version);
        var entity = db.LegalDocumentVersions.AsNoTracking().FirstOrDefault(x => x.DocumentKey == key && x.Version == versionCode);
        return entity is null ? null : ToDocumentDomain(entity);
    }

    public LegalDocumentVersion? GetActiveDocumentVersion(string documentKey)
    {
        var key = Normalize(documentKey);
        var entity = db.LegalDocumentVersions.AsNoTracking().Where(x => x.DocumentKey == key && x.IsActive).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return entity is null ? null : ToDocumentDomain(entity);
    }

    public IReadOnlyCollection<LegalDocumentVersion> ListDocumentVersions(string? documentKey = null)
    {
        var key = string.IsNullOrWhiteSpace(documentKey) ? null : Normalize(documentKey);
        var query = db.LegalDocumentVersions.AsNoTracking();
        if (key is not null)
        {
            query = query.Where(x => x.DocumentKey == key);
        }

        return query.OrderBy(x => x.DocumentKey).ThenByDescending(x => x.CreatedAt).Select(x => ToDocumentDomain(x)).ToArray();
    }

    public void SaveEvent(LegalEvidenceEvent item)
    {
        db.LegalEvents.Add(ToEventEntity(item with { ClientId = Normalize(item.ClientId) }));
        db.SaveChanges();
    }

    public IReadOnlyCollection<LegalEvidenceEvent> ListEventsForClient(string clientId, int limit = 100)
    {
        var normalized = Normalize(clientId);
        return db.LegalEvents.AsNoTracking().Where(x => x.ClientId == normalized).OrderByDescending(x => x.CreatedAt).Take(Math.Max(1, limit)).Select(x => ToEventDomain(x)).ToArray();
    }

    private static LegalDocumentVersionEntity ToDocumentEntity(LegalDocumentVersion x) => new()
    {
        Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id,
        DocumentKey = Normalize(x.DocumentKey),
        Version = Normalize(x.Version),
        TextHash = x.TextHash,
        Text = x.Text,
        Url = x.Url,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt.ToUniversalTime()
    };

    private static LegalDocumentVersion ToDocumentDomain(LegalDocumentVersionEntity x) => new(x.Id, x.DocumentKey, x.Version, x.TextHash, x.Text, x.Url, x.IsActive, x.CreatedAt);

    private static LegalEventEntity ToEventEntity(LegalEvidenceEvent x) => new()
    {
        Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id,
        EventType = x.EventType,
        ClientId = Normalize(x.ClientId),
        UserId = x.UserId,
        ImportBatchId = x.ImportBatchId,
        MailingId = x.MailingId,
        DocumentKey = x.DocumentKey,
        DocumentVersion = x.DocumentVersion,
        TextHash = x.TextHash,
        EventTextSnapshot = x.EventTextSnapshot,
        Result = x.Result,
        Ip = x.Ip,
        UserAgent = x.UserAgent,
        Route = x.Route,
        MetadataJson = x.MetadataJson,
        CreatedAt = x.CreatedAt.ToUniversalTime()
    };

    private static LegalEvidenceEvent ToEventDomain(LegalEventEntity x) => new(x.Id, x.EventType, x.ClientId, x.UserId, x.ImportBatchId, x.MailingId, x.DocumentKey, x.DocumentVersion, x.TextHash, x.EventTextSnapshot, x.Result, x.Ip, x.UserAgent, x.Route, x.MetadataJson, x.CreatedAt);

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

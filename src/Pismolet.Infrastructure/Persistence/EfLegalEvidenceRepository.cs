using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfLegalEvidenceRepository(LegalEvidenceDbContext db) : ILegalEvidenceRepository
{
    public void SaveDocumentVersion(LegalDocumentVersion version)
    {
        if (version.IsActive)
        {
            foreach (var activeVersion in db.LegalDocumentVersions.Where(x => x.DocumentKey == version.DocumentKey && x.IsActive))
            {
                activeVersion.IsActive = false;
            }
        }

        var existing = db.LegalDocumentVersions.SingleOrDefault(x => x.DocumentKey == version.DocumentKey && x.Version == version.Version);
        if (existing is null)
        {
            db.LegalDocumentVersions.Add(ToEntity(version));
        }
        else
        {
            existing.TextHash = version.TextHash;
            existing.Text = version.Text;
            existing.Url = version.Url;
            existing.IsActive = version.IsActive;
            existing.CreatedAt = version.CreatedAt;
        }

        db.SaveChanges();
    }

    public LegalDocumentVersion? GetDocumentVersion(string documentKey, string version) => db.LegalDocumentVersions
        .AsNoTracking()
        .Where(x => x.DocumentKey == documentKey && x.Version == version)
        .AsEnumerable()
        .Select(ToDomain)
        .SingleOrDefault();

    public LegalDocumentVersion? GetActiveDocumentVersion(string documentKey) => db.LegalDocumentVersions
        .AsNoTracking()
        .Where(x => x.DocumentKey == documentKey && x.IsActive)
        .OrderByDescending(x => x.CreatedAt)
        .AsEnumerable()
        .Select(ToDomain)
        .FirstOrDefault();

    public IReadOnlyCollection<LegalDocumentVersion> ListDocumentVersions(string? documentKey = null)
    {
        var query = db.LegalDocumentVersions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(documentKey))
        {
            query = query.Where(x => x.DocumentKey == documentKey.Trim());
        }

        return query
            .OrderBy(x => x.DocumentKey)
            .ThenByDescending(x => x.CreatedAt)
            .AsEnumerable()
            .Select(ToDomain)
            .ToList();
    }

    public void SaveEvent(LegalEvidenceEvent legalEvent)
    {
        db.LegalEvents.Add(ToEntity(legalEvent));
        db.SaveChanges();
    }

    public IReadOnlyCollection<LegalEvidenceEvent> ListEventsForClient(string clientId, int limit = 100)
    {
        var normalizedClientId = clientId.Trim().ToLowerInvariant();
        var take = Math.Clamp(limit, 1, 1000);

        return db.LegalEvents
            .AsNoTracking()
            .Where(x => x.ClientId == normalizedClientId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .AsEnumerable()
            .Select(ToDomain)
            .ToList();
    }

    private static LegalDocumentVersionEntity ToEntity(LegalDocumentVersion version) => new()
    {
        Id = version.Id,
        DocumentKey = version.DocumentKey,
        Version = version.Version,
        TextHash = version.TextHash,
        Text = version.Text,
        Url = version.Url,
        IsActive = version.IsActive,
        CreatedAt = version.CreatedAt
    };

    private static LegalEventEntity ToEntity(LegalEvidenceEvent legalEvent) => new()
    {
        Id = legalEvent.Id,
        EventType = legalEvent.EventType,
        ClientId = legalEvent.ClientId,
        UserId = legalEvent.UserId,
        ImportBatchId = legalEvent.ImportBatchId,
        MailingId = legalEvent.MailingId,
        DocumentKey = legalEvent.DocumentKey,
        DocumentVersion = legalEvent.DocumentVersion,
        TextHash = legalEvent.TextHash,
        EventTextSnapshot = legalEvent.EventTextSnapshot,
        Result = legalEvent.Result,
        Ip = legalEvent.Ip,
        UserAgent = legalEvent.UserAgent,
        Route = legalEvent.Route,
        MetadataJson = string.IsNullOrWhiteSpace(legalEvent.MetadataJson) ? "{}" : legalEvent.MetadataJson,
        CreatedAt = legalEvent.CreatedAt
    };

    private static LegalDocumentVersion ToDomain(LegalDocumentVersionEntity entity) => new(
        entity.Id,
        entity.DocumentKey,
        entity.Version,
        entity.TextHash,
        entity.Text,
        entity.Url,
        entity.IsActive,
        entity.CreatedAt);

    private static LegalEvidenceEvent ToDomain(LegalEventEntity entity) => new(
        entity.Id,
        entity.EventType,
        entity.ClientId,
        entity.UserId,
        entity.ImportBatchId,
        entity.MailingId,
        entity.DocumentKey,
        entity.DocumentVersion,
        entity.TextHash,
        entity.EventTextSnapshot,
        entity.Result,
        entity.Ip,
        entity.UserAgent,
        entity.Route,
        entity.MetadataJson,
        entity.CreatedAt);
}
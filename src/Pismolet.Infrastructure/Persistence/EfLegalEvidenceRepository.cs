using Pismolet.Web.Application.Legal;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfLegalEvidenceRepository(LegalEvidenceDbContext db) : ILegalEvidenceRepository
{
    public void SaveDocumentVersion(LegalDocumentVersion version) => throw new NotImplementedException();
    public LegalDocumentVersion? GetDocumentVersion(string documentKey, string version) => throw new NotImplementedException();
    public LegalDocumentVersion? GetActiveDocumentVersion(string documentKey) => throw new NotImplementedException();
    public IReadOnlyCollection<LegalDocumentVersion> ListDocumentVersions(string? documentKey = null) => throw new NotImplementedException();
    public void SaveEvent(LegalEvidenceEvent legalEvent) => throw new NotImplementedException();
    public IReadOnlyCollection<LegalEvidenceEvent> ListEventsForClient(string clientId, int limit = 100) => throw new NotImplementedException();
}

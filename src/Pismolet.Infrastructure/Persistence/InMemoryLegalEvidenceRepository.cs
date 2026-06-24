using System.Collections.Concurrent;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Domain.Legal;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryLegalEvidenceRepository : ILegalEvidenceRepository
{
    private readonly ConcurrentDictionary<(string DocumentKey, string Version), LegalDocumentVersion> _documents = new();
    private readonly ConcurrentDictionary<Guid, LegalEvidenceEvent> _events = new();

    public void SaveDocumentVersion(LegalDocumentVersion version)
    {
        var item = version with
        {
            DocumentKey = Normalize(version.DocumentKey),
            Version = Normalize(version.Version)
        };

        if (item.IsActive)
        {
            foreach (var pair in _documents.Where(pair => pair.Key.DocumentKey.Equals(item.DocumentKey, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _documents[pair.Key] = pair.Value with { IsActive = false };
            }
        }

        _documents[(item.DocumentKey, item.Version)] = item;
    }

    public LegalDocumentVersion? GetDocumentVersion(string documentKey, string version) => _documents.GetValueOrDefault((Normalize(documentKey), Normalize(version)));

    public LegalDocumentVersion? GetActiveDocumentVersion(string documentKey)
    {
        var normalized = Normalize(documentKey);
        return _documents.Values
            .Where(item => item.IsActive && item.DocumentKey.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
    }

    public IReadOnlyCollection<LegalDocumentVersion> ListDocumentVersions(string? documentKey = null)
    {
        var normalized = string.IsNullOrWhiteSpace(documentKey) ? null : Normalize(documentKey);
        return _documents.Values
            .Where(item => normalized is null || item.DocumentKey.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.DocumentKey, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.CreatedAt)
            .ToArray();
    }

    public void SaveEvent(LegalEvidenceEvent legalEvent) => _events[legalEvent.Id] = legalEvent with { ClientId = Normalize(legalEvent.ClientId) };

    public IReadOnlyCollection<LegalEvidenceEvent> ListEventsForClient(string clientId, int limit = 100)
    {
        var normalized = Normalize(clientId);
        return _events.Values
            .Where(item => item.ClientId.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

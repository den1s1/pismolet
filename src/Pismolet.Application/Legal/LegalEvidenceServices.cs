using System.Security.Cryptography;
using System.Text;
using Pismolet.Web.Domain.Legal;

namespace Pismolet.Web.Application.Legal;

public interface ILegalEvidenceRepository
{
    void SaveDocumentVersion(LegalDocumentVersion version);
    LegalDocumentVersion? GetDocumentVersion(string documentKey, string version);
    LegalDocumentVersion? GetActiveDocumentVersion(string documentKey);
    IReadOnlyCollection<LegalDocumentVersion> ListDocumentVersions(string? documentKey = null);
    void SaveEvent(LegalEvidenceEvent legalEvent);
    IReadOnlyCollection<LegalEvidenceEvent> ListEventsForClient(string clientId, int limit = 100);
}

public interface ILegalEvidenceService
{
    LegalDocumentVersion SaveDocumentVersion(LegalDocumentVersionDraft draft);
    LegalEvidenceEvent RecordEvent(LegalEvidenceEventDraft draft);
    string ComputeTextHash(string text);
}

public sealed record LegalDocumentVersionDraft(
    string DocumentKey,
    string Version,
    string Text,
    string? Url,
    bool IsActive = true);

public sealed record LegalEvidenceEventDraft(
    string EventType,
    string ClientId,
    string? UserId,
    Guid? ImportBatchId,
    Guid? MailingId,
    string? DocumentKey,
    string? DocumentVersion,
    string? TextHash,
    string? EventTextSnapshot,
    string Result,
    string? Ip,
    string? UserAgent,
    string? Route,
    string MetadataJson = "{}");

public sealed class LegalEvidenceService(ILegalEvidenceRepository repository) : ILegalEvidenceService
{
    public LegalDocumentVersion SaveDocumentVersion(LegalDocumentVersionDraft draft)
    {
        var documentKey = RequireNonEmpty(draft.DocumentKey, nameof(draft.DocumentKey));
        var version = RequireNonEmpty(draft.Version, nameof(draft.Version));
        var text = RequireNonEmpty(draft.Text, nameof(draft.Text));
        var item = new LegalDocumentVersion(
            Guid.NewGuid(),
            documentKey,
            version,
            ComputeTextHash(text),
            text,
            NormalizeOptional(draft.Url),
            draft.IsActive,
            DateTimeOffset.UtcNow);
        repository.SaveDocumentVersion(item);
        return item;
    }

    public LegalEvidenceEvent RecordEvent(LegalEvidenceEventDraft draft)
    {
        var eventType = RequireNonEmpty(draft.EventType, nameof(draft.EventType));
        var clientId = RequireNonEmpty(draft.ClientId, nameof(draft.ClientId)).ToLowerInvariant();
        var result = RequireNonEmpty(draft.Result, nameof(draft.Result));
        var snapshot = NormalizeOptional(draft.EventTextSnapshot);
        var textHash = NormalizeOptional(draft.TextHash) ?? (snapshot is null ? null : ComputeTextHash(snapshot));
        var legalEvent = new LegalEvidenceEvent(
            Guid.NewGuid(),
            eventType,
            clientId,
            NormalizeOptional(draft.UserId),
            draft.ImportBatchId,
            draft.MailingId,
            NormalizeOptional(draft.DocumentKey),
            NormalizeOptional(draft.DocumentVersion),
            textHash,
            snapshot,
            result,
            NormalizeOptional(draft.Ip),
            NormalizeOptional(draft.UserAgent),
            NormalizeOptional(draft.Route),
            string.IsNullOrWhiteSpace(draft.MetadataJson) ? "{}" : draft.MetadataJson.Trim(),
            DateTimeOffset.UtcNow);
        repository.SaveEvent(legalEvent);
        return legalEvent;
    }

    public string ComputeTextHash(string text)
    {
        var value = RequireNonEmpty(text, nameof(text));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} must not be empty.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

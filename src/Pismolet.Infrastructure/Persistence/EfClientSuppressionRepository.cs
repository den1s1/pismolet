using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfClientSuppressionRepository(PismoletDbContext db) : IClientSuppressionRepository
{
    public bool IsSuppressed(string clientId, string normalizedEmail) => db.ClientSuppressions.Any(x =>
        x.ClientId == Normalize(clientId) &&
        x.EmailNormalized == Normalize(normalizedEmail));

    public IReadOnlySet<string> GetSuppressedSet(string clientId, IEnumerable<string> normalizedEmails)
    {
        var normalizedClientId = Normalize(clientId);
        var emails = normalizedEmails
            .Select(Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (emails.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return db.ClientSuppressions
            .AsNoTracking()
            .Where(x => x.ClientId == normalizedClientId && emails.Contains(x.EmailNormalized))
            .Select(x => x.EmailNormalized)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ClientSuppression> ListRecent(int limit) => db.ClientSuppressions
        .AsNoTracking()
        .OrderByDescending(x => x.CreatedAt)
        .Take(Math.Max(1, limit))
        .Select(x => ToDomain(x))
        .ToArray();

    public ClientSuppression AddOrUpdate(ClientSuppression suppression)
    {
        var normalizedClientId = Normalize(suppression.ClientId);
        var normalizedEmail = Normalize(suppression.EmailNormalized);
        var existing = db.ClientSuppressions.FirstOrDefault(x =>
            x.ClientId == normalizedClientId &&
            x.EmailNormalized == normalizedEmail);

        if (existing is null)
        {
            var entity = ToEntity(suppression with
            {
                ClientId = normalizedClientId,
                EmailNormalized = normalizedEmail
            });
            db.ClientSuppressions.Add(entity);
            db.SaveChanges();
            return ToDomain(entity);
        }

        var touched = ToDomain(existing).Touch(suppression.SourceMailingId, suppression.SourceProviderMessageId);
        existing.Reason = touched.Reason.ToString();
        existing.SourceMailingId = touched.SourceMailingId;
        existing.SourceProviderMessageId = touched.SourceProviderMessageId;
        existing.LastSeenAt = touched.LastSeenAt.ToUniversalTime();
        db.SaveChanges();
        return ToDomain(existing);
    }

    private static ClientSuppressionEntity ToEntity(ClientSuppression item) => new()
    {
        Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
        ClientId = Normalize(item.ClientId),
        EmailNormalized = Normalize(item.EmailNormalized),
        Reason = item.Reason.ToString(),
        SourceMailingId = item.SourceMailingId,
        SourceProviderMessageId = item.SourceProviderMessageId,
        CreatedAt = item.CreatedAt.ToUniversalTime(),
        LastSeenAt = item.LastSeenAt.ToUniversalTime()
    };

    private static ClientSuppression ToDomain(ClientSuppressionEntity entity) => new(
        entity.Id,
        entity.ClientId,
        entity.EmailNormalized,
        Enum.TryParse<ClientSuppressionReason>(entity.Reason, out var reason) ? reason : ClientSuppressionReason.ManualBlock,
        entity.SourceMailingId,
        entity.SourceProviderMessageId,
        entity.CreatedAt,
        entity.LastSeenAt);

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

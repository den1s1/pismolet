using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, UserAccount> _users = new(StringComparer.OrdinalIgnoreCase);

    public bool Exists(string email) => _users.ContainsKey(email);

    public bool TryAdd(UserAccount user) => _users.TryAdd(user.Email, user);

    public UserAccount? GetByEmail(string email) => _users.GetValueOrDefault(email);

    public UserAccount? FindByConfirmationToken(string token) => _users.Values.FirstOrDefault(user => user.ConfirmationToken == token);

    public void Update(UserAccount user) => _users[user.Email] = user;
}

public sealed class InMemoryMailingRepository : IMailingRepository
{
    private readonly ConcurrentDictionary<Guid, Mailing> _items = new();

    public bool TryAdd(Mailing mailing) => _items.TryAdd(mailing.Id, mailing);

    public Mailing? Get(Guid id) => _items.GetValueOrDefault(id);

    public Mailing? GetForOwner(Guid id, string userEmail)
    {
        var mailing = Get(id);
        return mailing is not null && string.Equals(mailing.OwnerEmail, userEmail, StringComparison.OrdinalIgnoreCase)
            ? mailing
            : null;
    }

    public IReadOnlyCollection<Mailing> ListForOwner(string userEmail) => _items.Values
        .Where(mailing => string.Equals(mailing.OwnerEmail, userEmail, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(mailing => mailing.CreatedAt)
        .ToArray();

    public void Update(Mailing mailing) => _items[mailing.Id] = mailing;
}

public sealed class InMemoryGlobalSuppressionRepository : IGlobalSuppressionRepository
{
    private readonly ConcurrentDictionary<string, GlobalSuppression> _items = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSuppressed(string normalizedEmail) => _items.ContainsKey(Normalize(normalizedEmail));

    public IReadOnlySet<string> GetSuppressedSet(IEnumerable<string> normalizedEmails)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in normalizedEmails.Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_items.ContainsKey(email))
            {
                result.Add(email);
            }
        }

        return result;
    }

    public GlobalSuppression? GetByEmail(string normalizedEmail) => _items.GetValueOrDefault(Normalize(normalizedEmail));

    public GlobalSuppression AddOrGet(GlobalSuppression suppression)
    {
        var normalized = Normalize(suppression.EmailNormalized);
        var item = suppression with { EmailNormalized = normalized };
        return _items.GetOrAdd(normalized, item);
    }

    public void Add(string normalizedEmail)
    {
        var normalized = Normalize(normalizedEmail);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        AddOrGet(new GlobalSuppression(
            Guid.NewGuid(),
            normalized,
            string.Empty,
            GlobalSuppressionSource.Admin,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null));
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

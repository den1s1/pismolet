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
    private readonly ConcurrentDictionary<string, byte> _emails = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSuppressed(string normalizedEmail) => _emails.ContainsKey(normalizedEmail);

    public void Add(string normalizedEmail) => _emails.TryAdd(normalizedEmail, 0);
}

using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

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

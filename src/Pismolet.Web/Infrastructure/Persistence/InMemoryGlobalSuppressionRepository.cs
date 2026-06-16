using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryGlobalSuppressionRepository : IGlobalSuppressionRepository
{
    private readonly ConcurrentDictionary<string, byte> _emails = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSuppressed(string normalizedEmail) => _emails.ContainsKey(normalizedEmail);

    public void Add(string normalizedEmail) => _emails.TryAdd(normalizedEmail, 0);
}

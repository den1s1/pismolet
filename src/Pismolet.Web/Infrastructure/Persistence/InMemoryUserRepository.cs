using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Infrastructure.Persistence;

/// <summary>
/// Dev-only in-memory хранилище пользователей для Sprint 0/Sprint 1.
/// Production-замена должна быть вынесена за IUserRepository и хранить данные в БД.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, UserAccount> _users = new(StringComparer.OrdinalIgnoreCase);

    public bool Exists(string email) => _users.ContainsKey(email);

    public bool TryAdd(UserAccount user) => _users.TryAdd(user.Email, user);

    public UserAccount? GetByEmail(string email) => _users.GetValueOrDefault(email);

    public UserAccount? FindByConfirmationToken(string token) => _users.Values.FirstOrDefault(user => user.ConfirmationToken == token);

    public void Update(UserAccount user) => _users[user.Email] = user;
}

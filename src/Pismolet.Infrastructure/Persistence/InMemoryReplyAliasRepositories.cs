using System.Collections.Concurrent;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryClientReplyAliasRepository : IClientReplyAliasRepository
{
    private readonly ConcurrentDictionary<string, ClientReplyAlias> _byClient = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _aliasToClient = new(StringComparer.OrdinalIgnoreCase);

    public ClientReplyAlias? GetByClientId(string clientId) => _byClient.GetValueOrDefault(Normalize(clientId));

    public ClientReplyAlias? GetByAlias(string alias)
    {
        return _aliasToClient.TryGetValue(Normalize(alias), out var clientId)
            ? GetByClientId(clientId)
            : null;
    }

    public bool AliasExists(string alias) => _aliasToClient.ContainsKey(Normalize(alias));

    public ClientReplyAlias AddOrGet(ClientReplyAlias alias)
    {
        var normalizedClient = Normalize(alias.ClientId);
        if (_byClient.GetValueOrDefault(normalizedClient) is { } existing)
        {
            return existing;
        }

        Save(alias with { ClientId = normalizedClient, Alias = Normalize(alias.Alias) });
        return _byClient[normalizedClient];
    }

    public void Save(ClientReplyAlias alias)
    {
        var normalizedClient = Normalize(alias.ClientId);
        var normalizedAlias = Normalize(alias.Alias);
        if (_aliasToClient.TryGetValue(normalizedAlias, out var existingClient) && !string.Equals(existingClient, normalizedClient, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Reply alias уже занят другим клиентом.");
        }

        if (_byClient.TryGetValue(normalizedClient, out var previous))
        {
            _aliasToClient.TryRemove(previous.Alias, out _);
        }

        var now = DateTimeOffset.UtcNow;
        var item = alias with
        {
            Id = alias.Id == Guid.Empty ? Guid.NewGuid() : alias.Id,
            ClientId = normalizedClient,
            Alias = normalizedAlias,
            CreatedAt = alias.CreatedAt == default ? now : alias.CreatedAt.ToUniversalTime(),
            UpdatedAt = alias.UpdatedAt == default ? now : alias.UpdatedAt.ToUniversalTime()
        };

        _byClient[normalizedClient] = item;
        _aliasToClient[normalizedAlias] = normalizedClient;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed class InMemoryOutboundReplyMessageRepository : IOutboundReplyMessageRepository
{
    private readonly ConcurrentDictionary<string, OutboundReplyMessageMapping> _byMessageId = new(StringComparer.OrdinalIgnoreCase);

    public OutboundReplyMessageMapping? GetByMessageId(string messageId) => _byMessageId.GetValueOrDefault(OutboundReplyMessageMapping.NormalizeMessageId(messageId));

    public IReadOnlyCollection<OutboundReplyMessageMapping> FindByMessageIds(IEnumerable<string> messageIds) => messageIds
        .Select(GetByMessageId)
        .OfType<OutboundReplyMessageMapping>()
        .GroupBy(x => x.MessageId, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.First())
        .ToArray();

    public void Save(OutboundReplyMessageMapping mapping)
    {
        var item = mapping with
        {
            Id = mapping.Id == Guid.Empty ? Guid.NewGuid() : mapping.Id,
            MessageId = OutboundReplyMessageMapping.NormalizeMessageId(mapping.MessageId),
            ClientId = Normalize(mapping.ClientId),
            RecipientEmailNormalized = Normalize(mapping.RecipientEmailNormalized),
            ReplyAlias = Normalize(mapping.ReplyAlias),
            CreatedAt = mapping.CreatedAt == default ? DateTimeOffset.UtcNow : mapping.CreatedAt.ToUniversalTime()
        };
        _byMessageId[item.MessageId] = item;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

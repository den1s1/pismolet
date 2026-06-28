using System.Security.Cryptography;
using System.Text;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public interface IClientReplyAliasService
{
    ClientReplyAlias GetOrCreate(string clientId);

    string BuildCandidate(string clientId);
}

public sealed class ClientReplyAliasService(IClientReplyAliasRepository aliases, IEmailNormalizer normalizer) : IClientReplyAliasService
{
    public const int MinAliasLength = 3;
    public const int MaxAliasLength = 48;
    private const int MaxCollisionAttempts = 1000;

    private static readonly HashSet<string> ReservedAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "administrator",
        "abuse",
        "postmaster",
        "root",
        "support",
        "help",
        "hello",
        "info",
        "legal",
        "privacy",
        "security",
        "billing",
        "mail",
        "reply",
        "noreply",
        "no-reply",
        "mailer-daemon"
    };

    public ClientReplyAlias GetOrCreate(string clientId)
    {
        var normalizedClientId = normalizer.Normalize(clientId);
        if (string.IsNullOrWhiteSpace(normalizedClientId))
        {
            throw new ArgumentException("Client id is required for reply alias.", nameof(clientId));
        }

        if (aliases.GetByClientId(normalizedClientId) is { } existing)
        {
            return existing;
        }

        var baseAlias = BuildCandidate(normalizedClientId);
        for (var attempt = 1; attempt <= MaxCollisionAttempts; attempt++)
        {
            var candidate = BuildCollisionCandidate(baseAlias, attempt);
            if (aliases.AliasExists(candidate))
            {
                continue;
            }

            try
            {
                return aliases.AddOrGet(ClientReplyAlias.Create(normalizedClientId, candidate));
            }
            catch (InvalidOperationException)
            {
                // Alias мог быть занят параллельной операцией. Пробуем следующий suffix.
            }
        }

        var fallback = BuildHashFallback(normalizedClientId, 16);
        return aliases.AddOrGet(ClientReplyAlias.Create(normalizedClientId, fallback));
    }

    public string BuildCandidate(string clientId)
    {
        var normalizedClientId = normalizer.Normalize(clientId);
        var localPart = ExtractLocalPart(normalizedClientId);
        var candidate = NormalizeLocalPart(localPart);
        return IsUsable(candidate) ? candidate : BuildHashFallback(normalizedClientId, 8);
    }

    private static string ExtractLocalPart(string clientId)
    {
        var at = clientId.IndexOf('@');
        return at <= 0 ? clientId : clientId[..at];
    }

    private static string NormalizeLocalPart(string localPart)
    {
        var builder = new StringBuilder(localPart.Length);
        var previousWasSeparator = false;

        foreach (var ch in localPart.Trim().ToLowerInvariant())
        {
            var next = IsAllowedLetterOrDigit(ch)
                ? ch
                : IsAllowedSeparator(ch)
                    ? ch
                    : '-';
            var isSeparator = IsAllowedSeparator(next);
            if (isSeparator && previousWasSeparator)
            {
                continue;
            }

            builder.Append(next);
            previousWasSeparator = isSeparator;
        }

        var value = builder.ToString().Trim('.', '-', '_');
        if (value.Length > MaxAliasLength)
        {
            value = value[..MaxAliasLength].Trim('.', '-', '_');
        }

        return value;
    }

    private static string BuildCollisionCandidate(string baseAlias, int attempt)
    {
        if (attempt <= 1)
        {
            return baseAlias;
        }

        var suffix = $"-{attempt}";
        var availableBaseLength = Math.Max(MinAliasLength, MaxAliasLength - suffix.Length);
        var prefix = baseAlias.Length <= availableBaseLength
            ? baseAlias
            : baseAlias[..availableBaseLength].Trim('.', '-', '_');
        return prefix + suffix;
    }

    private static bool IsUsable(string alias) =>
        alias.Length is >= MinAliasLength and <= MaxAliasLength &&
        !ReservedAliases.Contains(alias) &&
        alias.Any(IsAllowedLetterOrDigit);

    private static bool IsAllowedLetterOrDigit(char ch) => ch is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsAllowedSeparator(char ch) => ch is '.' or '-' or '_';

    private static string BuildHashFallback(string clientId, int length)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId))).ToLowerInvariant();
        return $"client-{hash[..Math.Clamp(length, 8, 32)]}";
    }
}

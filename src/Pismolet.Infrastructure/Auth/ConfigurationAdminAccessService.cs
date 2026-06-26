using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Common;

namespace Pismolet.Web.Infrastructure.Auth;

public sealed class ConfigurationAdminAccessService(IConfiguration configuration) : IAdminAccessService
{
    private readonly object _sync = new();
    private HashSet<string>? _managedAdmins;

    public bool IsAdminEmail(string? email) => IsConfigAdminEmail(email) || IsManagedAdminEmail(email);

    public bool IsConfigAdminEmail(string? email)
    {
        var normalized = Normalize(email);
        return !string.IsNullOrWhiteSpace(normalized) && ReadConfigAdminEmails(configuration).Contains(normalized);
    }

    public bool IsManagedAdminEmail(string? email)
    {
        var normalized = Normalize(email);
        return !string.IsNullOrWhiteSpace(normalized) && ReadManagedAdmins().Contains(normalized);
    }

    public IReadOnlyCollection<string> ListManagedAdminEmails() => ReadManagedAdmins()
        .OrderBy(email => email, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void GrantAdmin(string email, string grantedByEmail)
    {
        var normalized = Normalize(email);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Не указан email пользователя.", nameof(email));
        }

        lock (_sync)
        {
            var admins = ReadManagedAdmins();
            admins.Add(normalized);
            SaveManagedAdmins(admins);
        }
    }

    public bool TryRevokeAdmin(string email, string revokedByEmail, out string error)
    {
        var normalized = Normalize(email);
        var actor = Normalize(revokedByEmail);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "Не указан email пользователя.";
            return false;
        }

        if (string.Equals(normalized, actor, StringComparison.OrdinalIgnoreCase))
        {
            error = "Нельзя снять админские права с самого себя.";
            return false;
        }

        if (IsConfigAdminEmail(normalized))
        {
            error = "Этот пользователь является администратором через конфигурацию. Уберите его из конфигурации сервера.";
            return false;
        }

        lock (_sync)
        {
            var admins = ReadManagedAdmins();
            admins.Remove(normalized);
            SaveManagedAdmins(admins);
        }

        error = string.Empty;
        return true;
    }

    private HashSet<string> ReadManagedAdmins()
    {
        lock (_sync)
        {
            if (_managedAdmins is not null)
            {
                return new HashSet<string>(_managedAdmins, StringComparer.OrdinalIgnoreCase);
            }

            _managedAdmins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = ManagedAdminsPath(configuration);
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        foreach (var item in Split(line))
                        {
                            var normalized = Normalize(item);
                            if (!string.IsNullOrWhiteSpace(normalized))
                            {
                                _managedAdmins.Add(normalized);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _managedAdmins.Clear();
            }

            return new HashSet<string>(_managedAdmins, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveManagedAdmins(HashSet<string> admins)
    {
        var path = ManagedAdminsPath(configuration);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var ordered = admins
            .Select(Normalize)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(email => email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        File.WriteAllLines(path, ordered);
        _managedAdmins = ordered.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string ManagedAdminsPath(IConfiguration configuration)
    {
        var configured = configuration["Admin:ManagedAdminsPath"]
            ?? configuration["Admin__ManagedAdminsPath"]
            ?? configuration["PISMOLET_ADMIN_MANAGED_PATH"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_ADMIN_MANAGED_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pismolet", "managed-admins.txt")
            : "/var/lib/pismolet/managed-admins.txt";
    }

    private static IReadOnlySet<string> ReadConfigAdminEmails(IConfiguration configuration)
    {
        var values = new List<string>();
        values.AddRange(Split(configuration["Admin:AllowedEmails"]));
        values.AddRange(Split(configuration["Admin:Emails"]));
        values.AddRange(Split(configuration["Pismolet:AdminEmails"]));
        values.AddRange(Split(configuration["PISMOLET_ADMIN_EMAILS"]));
        values.AddRange(Split(Environment.GetEnvironmentVariable("PISMOLET_ADMIN_EMAILS")));

        foreach (var child in configuration.GetSection("Admin:AllowedEmails").GetChildren())
        {
            values.AddRange(Split(child.Value));
        }

        return values
            .Select(Normalize)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static IEnumerable<string> Split(string? value) => string.IsNullOrWhiteSpace(value)
        ? Array.Empty<string>()
        : value.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

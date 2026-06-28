using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Admin;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryAdminNotificationSettingsRepository : IAdminNotificationSettingsRepository
{
    private readonly ConcurrentDictionary<string, AdminNotificationSettings> _items = new(StringComparer.OrdinalIgnoreCase);

    public AdminNotificationSettings Get(string adminEmail) => _items.GetValueOrDefault(Normalize(adminEmail)) ?? AdminNotificationSettings.Default;

    public void Save(string adminEmail, AdminNotificationSettings settings) => _items[Normalize(adminEmail)] = settings;

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed class FileAdminNotificationSettingsRepository(IConfiguration configuration) : IAdminNotificationSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private Dictionary<string, AdminNotificationSettings>? _items;

    public AdminNotificationSettings Get(string adminEmail)
    {
        var normalized = Normalize(adminEmail);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return AdminNotificationSettings.Default;
        }

        lock (_sync)
        {
            var items = Read();
            return items.GetValueOrDefault(normalized) ?? AdminNotificationSettings.Default;
        }
    }

    public void Save(string adminEmail, AdminNotificationSettings settings)
    {
        var normalized = Normalize(adminEmail);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (_sync)
        {
            var items = Read();
            items[normalized] = settings;
            Save(items);
        }
    }

    private Dictionary<string, AdminNotificationSettings> Read()
    {
        if (_items is not null)
        {
            return new Dictionary<string, AdminNotificationSettings>(_items, StringComparer.OrdinalIgnoreCase);
        }

        var path = SettingsPath(configuration);
        try
        {
            if (File.Exists(path))
            {
                var raw = File.ReadAllText(path);
                _items = JsonSerializer.Deserialize<Dictionary<string, AdminNotificationSettings>>(raw, JsonOptions)
                    ?? new Dictionary<string, AdminNotificationSettings>(StringComparer.OrdinalIgnoreCase);
                _items = _items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .ToDictionary(item => Normalize(item.Key), item => item.Value, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _items = new Dictionary<string, AdminNotificationSettings>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            _items = new Dictionary<string, AdminNotificationSettings>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, AdminNotificationSettings>(_items, StringComparer.OrdinalIgnoreCase);
    }

    private void Save(Dictionary<string, AdminNotificationSettings> items)
    {
        var path = SettingsPath(configuration);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var normalized = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => Normalize(item.Key), item => item.Value, StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(path, JsonSerializer.Serialize(normalized, JsonOptions));
        _items = normalized;
    }

    private static string SettingsPath(IConfiguration configuration)
    {
        var configured = configuration["Admin:NotificationSettingsPath"]
            ?? configuration["Admin__NotificationSettingsPath"]
            ?? configuration["PISMOLET_ADMIN_NOTIFICATION_SETTINGS_PATH"]
            ?? Environment.GetEnvironmentVariable("PISMOLET_ADMIN_NOTIFICATION_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pismolet", "admin-notifications.json")
            : "/var/lib/pismolet/admin-notifications.json";
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

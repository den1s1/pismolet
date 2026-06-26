using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class FileMailWarmupRuntimeSettingsRepository(IConfiguration configuration) : IMailWarmupRuntimeSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string StoragePath { get; } = ResolveStoragePath(configuration);

    public bool HasStoredSettings => File.Exists(StoragePath);

    public MailWarmupRuntimeSettings Get() => ReadCurrent(configuration);

    public MailWarmupSettingsSaveResult Save(MailWarmupRuntimeSettings settings)
    {
        var normalized = settings.Normalize();
        if (normalized.MaxPerMinute > 0 && normalized.MaxPerHour > 0 && normalized.MaxPerMinute > normalized.MaxPerHour)
        {
            return MailWarmupSettingsSaveResult.Failure("Лимит в минуту не должен быть больше лимита в час.", Get());
        }

        if (normalized.MaxPerHour > 0 && normalized.MaxPerDay > 0 && normalized.MaxPerHour > normalized.MaxPerDay)
        {
            return MailWarmupSettingsSaveResult.Failure("Лимит в час не должен быть больше лимита в день.", Get());
        }

        try
        {
            var directory = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = StoragePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(tempPath, StoragePath, overwrite: true);
            return MailWarmupSettingsSaveResult.Success(normalized);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return MailWarmupSettingsSaveResult.Failure($"Не удалось сохранить настройки warmup: {ex.Message}", Get());
        }
    }

    public static MailWarmupRuntimeSettings ReadCurrent(IConfiguration configuration)
    {
        var path = ResolveStoragePath(configuration);
        var stored = TryRead(path);
        if (stored is not null)
        {
            return stored.Normalize();
        }

        return new MailWarmupRuntimeSettings(
            MaxPerMinute: ReadInt(configuration, "MailWarmup:MaxPerMinute", MailWarmupRuntimeSettings.Default.MaxPerMinute, 0, MailWarmupRuntimeSettings.MaxLimitValue),
            MaxPerHour: ReadInt(configuration, "MailWarmup:MaxPerHour", MailWarmupRuntimeSettings.Default.MaxPerHour, 0, MailWarmupRuntimeSettings.MaxLimitValue),
            MaxPerDay: ReadInt(configuration, "MailWarmup:MaxPerDay", MailWarmupRuntimeSettings.Default.MaxPerDay, 0, MailWarmupRuntimeSettings.MaxLimitValue),
            MinSecondsBetweenSends: ReadInt(configuration, "MailWarmup:MinSecondsBetweenSends", MailWarmupRuntimeSettings.Default.MinSecondsBetweenSends, 0, MailWarmupRuntimeSettings.MaxDelaySeconds))
            .Normalize();
    }

    private static MailWarmupRuntimeSettings? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MailWarmupRuntimeSettings>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ResolveStoragePath(IConfiguration configuration) =>
        configuration["MailWarmup:SettingsPath"]
        ?? configuration["MailWarmup__SettingsPath"]
        ?? configuration["PISMOLET_MAIL_WARMUP_SETTINGS_PATH"]
        ?? Environment.GetEnvironmentVariable("PISMOLET_MAIL_WARMUP_SETTINGS_PATH")
        ?? "/var/lib/pismolet/mail-warmup-limits.json";

    private static int ReadInt(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        var value = configuration[key] ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)] ?? Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal));
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }
}

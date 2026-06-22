using System.Text.Json;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed record PostfixDeliveryAutomationSettings(bool Enabled, int IntervalSeconds)
{
    public const int DefaultIntervalSeconds = 60;
    public const int MinIntervalSeconds = 10;
    public const int MaxIntervalSeconds = 86_400;

    public static PostfixDeliveryAutomationSettings Default => new(Enabled: true, IntervalSeconds: DefaultIntervalSeconds);

    public PostfixDeliveryAutomationSettings Normalize() => new(
        Enabled,
        Math.Clamp(IntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds));
}

public sealed record PostfixDeliveryAutomationSettingsOptions(string SettingsPath, int DefaultIntervalSeconds)
{
    public static PostfixDeliveryAutomationSettingsOptions ProductionDefault => new(
        "/var/lib/pismolet/postfix-delivery-settings.json",
        PostfixDeliveryAutomationSettings.DefaultIntervalSeconds);
}

public interface IPostfixDeliveryAutomationSettingsRepository
{
    PostfixDeliveryAutomationSettings Get();

    void Save(PostfixDeliveryAutomationSettings settings);
}

public sealed class FilePostfixDeliveryAutomationSettingsRepository(PostfixDeliveryAutomationSettingsOptions options)
    : IPostfixDeliveryAutomationSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();

    public PostfixDeliveryAutomationSettings Get()
    {
        lock (_sync)
        {
            if (!File.Exists(options.SettingsPath))
            {
                return BuildDefault();
            }

            try
            {
                var raw = File.ReadAllText(options.SettingsPath);
                var dto = JsonSerializer.Deserialize<PostfixDeliveryAutomationSettingsDto>(raw, JsonOptions);
                if (dto is null)
                {
                    return BuildDefault();
                }

                return new PostfixDeliveryAutomationSettings(dto.Enabled, dto.IntervalSeconds).Normalize();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return BuildDefault();
            }
        }
    }

    public void Save(PostfixDeliveryAutomationSettings settings)
    {
        var normalized = settings.Normalize();
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(options.SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dto = new PostfixDeliveryAutomationSettingsDto(normalized.Enabled, normalized.IntervalSeconds);
            var tempPath = options.SettingsPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(dto, JsonOptions));
            File.Move(tempPath, options.SettingsPath, overwrite: true);
        }
    }

    private PostfixDeliveryAutomationSettings BuildDefault() => new(
        Enabled: true,
        IntervalSeconds: Math.Clamp(
            options.DefaultIntervalSeconds,
            PostfixDeliveryAutomationSettings.MinIntervalSeconds,
            PostfixDeliveryAutomationSettings.MaxIntervalSeconds));

    private sealed record PostfixDeliveryAutomationSettingsDto(bool Enabled, int IntervalSeconds);
}

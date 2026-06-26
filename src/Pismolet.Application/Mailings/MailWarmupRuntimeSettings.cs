namespace Pismolet.Web.Application.Mailings;

public sealed record MailWarmupRuntimeSettings(
    int MaxPerMinute,
    int MaxPerHour,
    int MaxPerDay,
    int MinSecondsBetweenSends)
{
    public const int MaxLimitValue = 100000;
    public const int MaxDelaySeconds = 86400;

    public static MailWarmupRuntimeSettings Default { get; } = new(
        MaxPerMinute: 10,
        MaxPerHour: 100,
        MaxPerDay: 300,
        MinSecondsBetweenSends: 6);

    public MailWarmupRuntimeSettings Normalize() => new(
        MaxPerMinute: Math.Clamp(MaxPerMinute, 0, MaxLimitValue),
        MaxPerHour: Math.Clamp(MaxPerHour, 0, MaxLimitValue),
        MaxPerDay: Math.Clamp(MaxPerDay, 0, MaxLimitValue),
        MinSecondsBetweenSends: Math.Clamp(MinSecondsBetweenSends, 0, MaxDelaySeconds));
}

public sealed record MailWarmupSettingsSaveResult(bool Ok, string Error, MailWarmupRuntimeSettings Settings)
{
    public static MailWarmupSettingsSaveResult Success(MailWarmupRuntimeSettings settings) => new(true, string.Empty, settings);

    public static MailWarmupSettingsSaveResult Failure(string error, MailWarmupRuntimeSettings settings) => new(false, error, settings);
}

public interface IMailWarmupRuntimeSettingsRepository
{
    string StoragePath { get; }

    bool HasStoredSettings { get; }

    MailWarmupRuntimeSettings Get();

    MailWarmupSettingsSaveResult Save(MailWarmupRuntimeSettings settings);
}

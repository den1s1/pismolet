using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Domain.Users;

public static class ClientStatuses
{
    public const string Active = "active";
    public const string Blocked = "blocked";

    public static string ToRu(string status) => string.Equals(status, Blocked, StringComparison.OrdinalIgnoreCase)
        ? "Заблокирован"
        : "Активен";
}

public sealed record ClientProfile(
    string Status,
    int DailySendLimit,
    int TotalSendLimit,
    bool PremoderationRequired,
    DateTimeOffset? BlockedAt = null,
    string? BlockedByAdminEmail = null,
    string? BlockReason = null,
    DateTimeOffset? UnblockedAt = null,
    string? UnblockedByAdminEmail = null,
    DateTimeOffset? LimitChangedAt = null,
    string? LimitChangedByAdminEmail = null,
    DateTimeOffset? PremoderationChangedAt = null,
    string? PremoderationChangedByAdminEmail = null)
{
    public bool IsBlocked => string.Equals(Status, ClientStatuses.Blocked, StringComparison.OrdinalIgnoreCase);

    public static ClientProfile NewClientDefault() => NewClientDefault(AdminMvpSettings.Default);

    public static ClientProfile NewClientDefault(AdminMvpSettings settings) => new(
        Status: ClientStatuses.Active,
        DailySendLimit: settings.DefaultDailySendLimit,
        TotalSendLimit: settings.DefaultTotalSendLimit,
        PremoderationRequired: settings.PremoderationForNewClients);

    public ClientProfile Block(string adminEmail, string? reason) => this with
    {
        Status = ClientStatuses.Blocked,
        BlockedAt = DateTimeOffset.UtcNow,
        BlockedByAdminEmail = Normalize(adminEmail),
        BlockReason = string.IsNullOrWhiteSpace(reason) ? "Заблокировано администратором" : reason.Trim(),
        UnblockedAt = null,
        UnblockedByAdminEmail = null
    };

    public ClientProfile Unblock(string adminEmail) => this with
    {
        Status = ClientStatuses.Active,
        UnblockedAt = DateTimeOffset.UtcNow,
        UnblockedByAdminEmail = Normalize(adminEmail)
    };

    public ClientProfile WithDailyLimit(int dailyLimit, string adminEmail) => this with
    {
        DailySendLimit = dailyLimit,
        LimitChangedAt = DateTimeOffset.UtcNow,
        LimitChangedByAdminEmail = Normalize(adminEmail)
    };

    public ClientProfile WithPremoderation(bool required, string adminEmail) => this with
    {
        PremoderationRequired = required,
        PremoderationChangedAt = DateTimeOffset.UtcNow,
        PremoderationChangedByAdminEmail = Normalize(adminEmail)
    };

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed record AdminMvpSettings(
    decimal PricePerRecipient,
    string Currency,
    bool PremoderationForNewClients,
    int DefaultDailySendLimit,
    int DefaultTotalSendLimit,
    int ReplyBodyRetentionDays,
    bool DevWebhookPagesEnabled,
    DateTimeOffset UpdatedAt,
    string UpdatedByAdminEmail)
{
    public static AdminMvpSettings Default { get; } = new(
        PricePerRecipient: 1m,
        Currency: "RUB",
        PremoderationForNewClients: true,
        DefaultDailySendLimit: 1000,
        DefaultTotalSendLimit: 10000,
        ReplyBodyRetentionDays: 14,
        DevWebhookPagesEnabled: false,
        UpdatedAt: DateTimeOffset.UtcNow,
        UpdatedByAdminEmail: "system");

    public void EnsureValid()
    {
        if (PricePerRecipient < 0) throw new ArgumentOutOfRangeException(nameof(PricePerRecipient), "Цена письма не может быть отрицательной.");
        if (!string.Equals(Currency, "RUB", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Для MVP поддерживается только RUB.", nameof(Currency));
        if (DefaultDailySendLimit < 0) throw new ArgumentOutOfRangeException(nameof(DefaultDailySendLimit), "Дневной лимит не может быть отрицательным.");
        if (DefaultTotalSendLimit < 0) throw new ArgumentOutOfRangeException(nameof(DefaultTotalSendLimit), "Общий лимит не может быть отрицательным.");
        if (ReplyBodyRetentionDays is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(ReplyBodyRetentionDays), "Retention ответов должен быть от 1 до 60 дней.");
    }
}

public sealed record UserAccount(
    string Email,
    string PasswordHash,
    string DisplayName,
    string ConfirmationToken,
    bool EmailConfirmed,
    ClientProfile Profile,
    List<Mailing> Mailings);
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Admin;

public sealed record AdminActionResult(bool Ok, string Error)
{
    public static AdminActionResult Success() => new(true, string.Empty);
    public static AdminActionResult Failure(string error) => new(false, error);
}

public sealed record AdminDashboardSnapshot(
    int ClientsTotal,
    int ClientsBlocked,
    int ClientsPremoderation,
    int MailingsTotal,
    int MailingsBlocked,
    int MailingsReviewRequired,
    int MailingsFailed,
    int Complaints,
    int HardBounces,
    int GlobalSuppressions,
    IReadOnlyCollection<AuditRecord> RecentAudit);

public interface IAdminOperationService
{
    AdminDashboardSnapshot GetDashboard();
    AdminActionResult BlockClient(string clientEmail, string adminEmail, string? reason, RequestMetadata request);
    AdminActionResult UnblockClient(string clientEmail, string adminEmail, RequestMetadata request);
    AdminActionResult UpdateDailyLimit(string clientEmail, int newDailyLimit, string adminEmail, RequestMetadata request);
    AdminActionResult SetClientPremoderation(string clientEmail, bool required, string adminEmail, RequestMetadata request);
    AdminActionResult BlockMailing(Guid mailingId, string adminEmail, string? reason, RequestMetadata request);
    AdminActionResult UnblockMailing(Guid mailingId, string adminEmail, RequestMetadata request);
    AdminActionResult UpdateMvpSettings(AdminMvpSettings settings, string adminEmail, RequestMetadata request);
}

public sealed class AdminOperationService(
    IUserRepository users,
    IMailingRepository mailings,
    IPaymentRepository payments,
    IPriceSettingsRepository priceSettings,
    IAdminMvpSettingsRepository mvpSettings,
    IProviderWebhookEventRepository providerEvents,
    IGlobalSuppressionRepository globalSuppressions,
    IAuditLogger auditLogger) : IAdminOperationService
{
    private const int MaxDailyLimit = 100000;

    public AdminDashboardSnapshot GetDashboard()
    {
        var allUsers = users.ListAll();
        var allMailings = mailings.ListAll();
        var webhooks = providerEvents.ListRecent(500);
        return new AdminDashboardSnapshot(
            allUsers.Count,
            allUsers.Count(user => user.Profile.IsBlocked),
            allUsers.Count(user => user.Profile.PremoderationRequired),
            allMailings.Count,
            allMailings.Count(mailing => mailing.Status == MailingStatus.Blocked),
            allMailings.Count(mailing => mailing.Status == MailingStatus.ReviewRequired),
            allMailings.Count(mailing => mailing.Status == MailingStatus.Failed),
            webhooks.Count(item => item.EventType == ProviderWebhookEventType.Complaint),
            webhooks.Count(item => item.EventType == ProviderWebhookEventType.HardBounce),
            globalSuppressions.ListAll().Count,
            auditLogger.GetRecords().Take(12).ToArray());
    }

    public AdminActionResult BlockClient(string clientEmail, string adminEmail, string? reason, RequestMetadata request)
    {
        var user = users.GetByEmail(N(clientEmail));
        if (user is null) return AdminActionResult.Failure("Клиент не найден.");
        if (user.Profile.IsBlocked) return AdminActionResult.Success();
        var updated = user with { Profile = user.Profile.Block(adminEmail, reason) };
        users.Update(updated);
        Audit(adminEmail, "admin_client_blocked", request, $"client={updated.Email};old={user.Profile.Status};new={updated.Profile.Status};reason={Safe(reason)}");
        return AdminActionResult.Success();
    }

    public AdminActionResult UnblockClient(string clientEmail, string adminEmail, RequestMetadata request)
    {
        var user = users.GetByEmail(N(clientEmail));
        if (user is null) return AdminActionResult.Failure("Клиент не найден.");
        var updated = user with { Profile = user.Profile.Unblock(adminEmail) };
        users.Update(updated);
        Audit(adminEmail, "admin_client_unblocked", request, $"client={updated.Email};old={user.Profile.Status};new={updated.Profile.Status}");
        return AdminActionResult.Success();
    }

    public AdminActionResult UpdateDailyLimit(string clientEmail, int newDailyLimit, string adminEmail, RequestMetadata request)
    {
        if (newDailyLimit < 0 || newDailyLimit > MaxDailyLimit) return AdminActionResult.Failure($"Дневной лимит должен быть от 0 до {MaxDailyLimit}.");
        var user = users.GetByEmail(N(clientEmail));
        if (user is null) return AdminActionResult.Failure("Клиент не найден.");
        var old = user.Profile.DailySendLimit;
        var updated = user with { Profile = user.Profile.WithDailyLimit(newDailyLimit, adminEmail) };
        users.Update(updated);
        Audit(adminEmail, "admin_client_daily_limit_changed", request, $"client={updated.Email};old={old};new={newDailyLimit}");
        return AdminActionResult.Success();
    }

    public AdminActionResult SetClientPremoderation(string clientEmail, bool required, string adminEmail, RequestMetadata request)
    {
        var user = users.GetByEmail(N(clientEmail));
        if (user is null) return AdminActionResult.Failure("Клиент не найден.");
        var old = user.Profile.PremoderationRequired;
        var updated = user with { Profile = user.Profile.WithPremoderation(required, adminEmail) };
        users.Update(updated);
        Audit(adminEmail, "admin_client_premoderation_changed", request, $"client={updated.Email};old={old};new={required}");
        return AdminActionResult.Success();
    }

    public AdminActionResult BlockMailing(Guid mailingId, string adminEmail, string? reason, RequestMetadata request)
    {
        var mailing = mailings.Get(mailingId);
        if (mailing is null) return AdminActionResult.Failure("Рассылка не найдена.");
        if (mailing.Status == MailingStatus.Blocked) return AdminActionResult.Success();
        var old = mailing.Status;
        mailings.Update(mailing.WithStatus(MailingStatus.Blocked));
        Audit(adminEmail, "admin_mailing_blocked", request, $"mailingId={mailing.Id};client={mailing.OwnerEmail};old={old};new={MailingStatus.Blocked};reason={Safe(reason)}");
        return AdminActionResult.Success();
    }

    public AdminActionResult UnblockMailing(Guid mailingId, string adminEmail, RequestMetadata request)
    {
        var mailing = mailings.Get(mailingId);
        if (mailing is null) return AdminActionResult.Failure("Рассылка не найдена.");
        if (mailing.Status != MailingStatus.Blocked) return AdminActionResult.Success();
        mailings.Update(mailing.WithStatus(MailingStatus.Approved));
        Audit(adminEmail, "admin_mailing_unblocked", request, $"mailingId={mailing.Id};client={mailing.OwnerEmail};old={MailingStatus.Blocked};new={MailingStatus.Approved}");
        return AdminActionResult.Success();
    }

    public AdminActionResult UpdateMvpSettings(AdminMvpSettings settings, string adminEmail, RequestMetadata request)
    {
        try
        {
            settings.EnsureValid();
            var old = mvpSettings.Get();
            var normalized = settings with { UpdatedAt = DateTimeOffset.UtcNow, UpdatedByAdminEmail = N(adminEmail) };
            mvpSettings.Save(normalized);
            priceSettings.Save(new PriceSettings(Guid.NewGuid(), normalized.PricePerRecipient, normalized.Currency, DateTimeOffset.UtcNow, true));
            Audit(adminEmail, "admin_mvp_settings_changed", request, $"oldPrice={old.PricePerRecipient};newPrice={normalized.PricePerRecipient};oldPremoderation={old.PremoderationForNewClients};newPremoderation={normalized.PremoderationForNewClients};oldDailyLimit={old.DefaultDailySendLimit};newDailyLimit={normalized.DefaultDailySendLimit};replyRetentionDays={normalized.ReplyBodyRetentionDays}");
            return AdminActionResult.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AdminActionResult.Failure(ex.Message);
        }
    }

    private void Audit(string adminEmail, string action, RequestMetadata request, string context) => auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, N(adminEmail), action, request.Ip, request.UserAgent, context));

    private static string N(string value) => value.Trim().ToLowerInvariant();

    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ').Trim();
}

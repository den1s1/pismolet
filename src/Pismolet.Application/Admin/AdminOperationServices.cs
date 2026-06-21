using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Admin;

public sealed record AdminActionResult(bool Ok, string Error) { public static AdminActionResult Success() => new(true, string.Empty); public static AdminActionResult Failure(string error) => new(false, error); }
public sealed record AdminDashboardSnapshot(int ClientsTotal, int ClientsBlocked, int ClientsPremoderation, int MailingsTotal, int MailingsBlocked, int MailingsReviewRequired, int MailingsFailed, int Complaints, int HardBounces, int GlobalSuppressions, IReadOnlyCollection<AuditRecord> RecentAudit);

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

public sealed class AdminOperationService(IUserRepository users, IMailingRepository mailings, IPriceSettingsRepository priceSettings, IAdminMvpSettingsRepository mvpSettings, ISendEventRepository sendEvents, IAuditLogger auditLogger) : IAdminOperationService
{
    private const int MaxDailyLimit = 100000;
    public AdminDashboardSnapshot GetDashboard()
    {
        var allUsers = users.ListAll();
        var allMailings = allUsers.SelectMany(user => mailings.ListForOwner(user.Email)).ToArray();
        var summaries = allMailings.Select(m => sendEvents.GetSummary(m.Id, m.LastImportStats.Accepted)).ToArray();
        return new AdminDashboardSnapshot(allUsers.Count, allUsers.Count(u => u.Profile.IsBlocked), allUsers.Count(u => u.Profile.PremoderationRequired), allMailings.Length, allMailings.Count(m => m.Status == MailingStatus.Blocked), allMailings.Count(m => m.Status == MailingStatus.ReviewRequired), allMailings.Count(m => m.Status == MailingStatus.Failed), summaries.Sum(s => s.Complaints), summaries.Sum(s => s.HardBounced), allMailings.Sum(m => m.LastImportStats.GloballySuppressed), auditLogger.GetRecords().Take(12).ToArray());
    }
    public AdminActionResult BlockClient(string clientEmail, string adminEmail, string? reason, RequestMetadata request) { var u = users.GetByEmail(N(clientEmail)); if (u is null) return AdminActionResult.Failure("Клиент не найден."); if (u.Profile.IsBlocked) return AdminActionResult.Success(); var next = u with { Profile = u.Profile.Block(adminEmail, reason) }; users.Update(next); A(adminEmail, "admin_client_blocked", request, $"client={next.Email};old={u.Profile.Status};new={next.Profile.Status}"); return AdminActionResult.Success(); }
    public AdminActionResult UnblockClient(string clientEmail, string adminEmail, RequestMetadata request) { var u = users.GetByEmail(N(clientEmail)); if (u is null) return AdminActionResult.Failure("Клиент не найден."); var next = u with { Profile = u.Profile.Unblock(adminEmail) }; users.Update(next); A(adminEmail, "admin_client_unblocked", request, $"client={next.Email};old={u.Profile.Status};new={next.Profile.Status}"); return AdminActionResult.Success(); }
    public AdminActionResult UpdateDailyLimit(string clientEmail, int newDailyLimit, string adminEmail, RequestMetadata request) { if (newDailyLimit < 0 || newDailyLimit > MaxDailyLimit) return AdminActionResult.Failure($"Дневной лимит должен быть от 0 до {MaxDailyLimit}."); var u = users.GetByEmail(N(clientEmail)); if (u is null) return AdminActionResult.Failure("Клиент не найден."); var next = u with { Profile = u.Profile.WithDailyLimit(newDailyLimit, adminEmail) }; users.Update(next); A(adminEmail, "admin_client_daily_limit_changed", request, $"client={next.Email};old={u.Profile.DailySendLimit};new={newDailyLimit}"); return AdminActionResult.Success(); }
    public AdminActionResult SetClientPremoderation(string clientEmail, bool required, string adminEmail, RequestMetadata request) { var u = users.GetByEmail(N(clientEmail)); if (u is null) return AdminActionResult.Failure("Клиент не найден."); var next = u with { Profile = u.Profile.WithPremoderation(required, adminEmail) }; users.Update(next); A(adminEmail, "admin_client_premoderation_changed", request, $"client={next.Email};old={u.Profile.PremoderationRequired};new={required}"); return AdminActionResult.Success(); }
    public AdminActionResult BlockMailing(Guid mailingId, string adminEmail, string? reason, RequestMetadata request) { var m = mailings.Get(mailingId); if (m is null) return AdminActionResult.Failure("Рассылка не найдена."); if (m.Status == MailingStatus.Blocked) return AdminActionResult.Success(); mailings.Update(m.WithStatus(MailingStatus.Blocked)); A(adminEmail, "admin_mailing_blocked", request, $"mailingId={m.Id};client={m.OwnerEmail};old={m.Status};new={MailingStatus.Blocked}"); return AdminActionResult.Success(); }
    public AdminActionResult UnblockMailing(Guid mailingId, string adminEmail, RequestMetadata request) { var m = mailings.Get(mailingId); if (m is null) return AdminActionResult.Failure("Рассылка не найдена."); if (m.Status != MailingStatus.Blocked) return AdminActionResult.Success(); mailings.Update(m.WithStatus(MailingStatus.Approved)); A(adminEmail, "admin_mailing_unblocked", request, $"mailingId={m.Id};client={m.OwnerEmail};old={MailingStatus.Blocked};new={MailingStatus.Approved}"); return AdminActionResult.Success(); }
    public AdminActionResult UpdateMvpSettings(AdminMvpSettings settings, string adminEmail, RequestMetadata request) { try { settings.EnsureValid(); var old = mvpSettings.Get(); var next = settings with { UpdatedAt = DateTimeOffset.UtcNow, UpdatedByAdminEmail = N(adminEmail) }; mvpSettings.Save(next); priceSettings.Save(new PriceSettings(Guid.NewGuid(), next.PricePerRecipient, next.Currency, DateTimeOffset.UtcNow, true)); A(adminEmail, "admin_mvp_settings_changed", request, $"oldPrice={old.PricePerRecipient};newPrice={next.PricePerRecipient};oldDailyLimit={old.DefaultDailySendLimit};newDailyLimit={next.DefaultDailySendLimit}"); return AdminActionResult.Success(); } catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException) { return AdminActionResult.Failure(ex.Message); } }
    private void A(string admin, string action, RequestMetadata r, string c) => auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, N(admin), action, r.Ip, r.UserAgent, c));
    private static string N(string value) => value.Trim().ToLowerInvariant();
}

using System.Text.Json;
using Pismolet.Web.Application.Admin;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class AdminUserRemovalService(
    IUserRepository users,
    IMailingRepository mailings,
    IAdminAccessService admins,
    IAuditLogger auditLogger) : IAdminUserRemovalService
{
    public AdminUserRemovalResult RemoveUser(string targetEmail, string adminEmail, RequestMetadata request)
    {
        var normalizedTarget = NormalizeEmail(targetEmail);
        var normalizedAdmin = NormalizeEmail(adminEmail);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return AdminUserRemovalResult.Failure("Укажите пользователя для удаления.");
        }

        if (users.GetByEmail(normalizedTarget) is null)
        {
            return AdminUserRemovalResult.Failure("Пользователь не найден.");
        }

        if (string.Equals(normalizedTarget, normalizedAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return AdminUserRemovalResult.Failure("Нельзя удалить собственный аккаунт администратора.");
        }

        if (admins.IsConfigAdminEmail(normalizedTarget))
        {
            return AdminUserRemovalResult.Failure("Нельзя удалить администратора, заданного в конфигурации сервера.");
        }

        if (admins.IsManagedAdminEmail(normalizedTarget))
        {
            return AdminUserRemovalResult.Failure("Сначала снимите админские права, затем удалите пользователя.");
        }

        var removedMailings = mailings.RemoveForOwner(normalizedTarget);
        users.Remove(normalizedTarget);

        auditLogger.Write(new AuditRecord(
            DateTimeOffset.UtcNow,
            normalizedAdmin,
            "admin_user_removed",
            request.Ip,
            request.UserAgent,
            JsonSerializer.Serialize(new
            {
                targetEmail = normalizedTarget,
                removedMailings
            })));

        return AdminUserRemovalResult.Success(removedMailings);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

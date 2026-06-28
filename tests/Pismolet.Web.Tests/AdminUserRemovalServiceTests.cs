using Pismolet.Web.Application.Common;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class AdminUserRemovalServiceTests
{
    private const string AdminEmail = "admin@example.test";
    private const string TargetEmail = "delete-me@example.test";

    [Fact]
    public void Remove_user_removes_login_and_owned_mailings_and_writes_audit()
    {
        var users = new InMemoryUserRepository();
        var mailings = new InMemoryMailingRepository();
        var audit = new InMemoryAuditLogger();
        users.TryAdd(User(AdminEmail));
        users.TryAdd(User(TargetEmail));
        users.TryAdd(User("other@example.test"));
        mailings.TryAdd(Mailing(TargetEmail, "First"));
        mailings.TryAdd(Mailing(TargetEmail, "Second"));
        mailings.TryAdd(Mailing("other@example.test", "Other"));
        var service = new AdminUserRemovalService(users, mailings, new FakeAdminAccess(), audit);

        var result = service.RemoveUser(TargetEmail, AdminEmail, Request());

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.MailingsRemoved);
        Assert.Null(users.GetByEmail(TargetEmail));
        Assert.Empty(mailings.ListForOwner(TargetEmail));
        Assert.NotNull(users.GetByEmail("other@example.test"));
        Assert.Single(mailings.ListForOwner("other@example.test"));
        var record = Assert.Single(audit.GetRecords());
        Assert.Equal("admin_user_removed", record.EventType);
        Assert.Equal(AdminEmail, record.User);
        Assert.Contains(TargetEmail, record.Context);
    }

    [Theory]
    [InlineData(TargetEmail, TargetEmail, null, null, "Нельзя удалить собственный аккаунт администратора.")]
    [InlineData("root@example.test", AdminEmail, "root@example.test", null, "Нельзя удалить администратора, заданного в конфигурации сервера.")]
    [InlineData("managed@example.test", AdminEmail, null, "managed@example.test", "Сначала снимите админские права, затем удалите пользователя.")]
    public void Remove_user_blocks_self_config_and_managed_admins(
        string targetEmail,
        string adminEmail,
        string? configAdmin,
        string? managedAdmin,
        string expectedError)
    {
        var users = new InMemoryUserRepository();
        var mailings = new InMemoryMailingRepository();
        users.TryAdd(User(targetEmail));
        mailings.TryAdd(Mailing(targetEmail, "Protected"));
        var service = new AdminUserRemovalService(
            users,
            mailings,
            new FakeAdminAccess(configAdmin is null ? Array.Empty<string>() : new[] { configAdmin }, managedAdmin is null ? Array.Empty<string>() : new[] { managedAdmin }),
            new InMemoryAuditLogger());

        var result = service.RemoveUser(targetEmail, adminEmail, Request());

        Assert.False(result.Ok);
        Assert.Equal(expectedError, result.Error);
        Assert.NotNull(users.GetByEmail(targetEmail));
        Assert.Single(mailings.ListForOwner(targetEmail));
    }

    private static UserAccount User(string email) => new(
        email.Trim().ToLowerInvariant(),
        "hash",
        email,
        $"token-{email}",
        EmailConfirmed: true,
        ClientProfile.NewClientDefault(),
        new List<Mailing>())
    {
        Phone = TestPhone(email)
    };

    private static string TestPhone(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var checksum = 0;
        foreach (var ch in normalized)
        {
            checksum = ((checksum * 31) + ch) % 10_000_000;
        }

        return $"+7999{checksum:0000000}";
    }

    private static Mailing Mailing(string ownerEmail, string subject) => Pismolet.Web.Domain.Mailings.Mailing.Draft(ownerEmail, subject) with
    {
        Id = Guid.NewGuid()
    };

    private static RequestMetadata Request() => new("127.0.0.1", "admin-user-removal-service-tests");

    private sealed class FakeAdminAccess(
        IReadOnlyCollection<string>? configAdmins = null,
        IReadOnlyCollection<string>? managedAdmins = null) : IAdminAccessService
    {
        private readonly HashSet<string> _configAdmins = (configAdmins ?? Array.Empty<string>()).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _managedAdmins = (managedAdmins ?? Array.Empty<string>()).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool IsAdminEmail(string? email) => IsConfigAdminEmail(email) || IsManagedAdminEmail(email);

        public bool IsConfigAdminEmail(string? email) => !string.IsNullOrWhiteSpace(email) && _configAdmins.Contains(Normalize(email));

        public bool IsManagedAdminEmail(string? email) => !string.IsNullOrWhiteSpace(email) && _managedAdmins.Contains(Normalize(email));

        public IReadOnlyCollection<string> ListManagedAdminEmails() => _managedAdmins.ToArray();

        public void GrantAdmin(string email, string grantedByEmail) => _managedAdmins.Add(Normalize(email));

        public bool TryRevokeAdmin(string email, string revokedByEmail, out string error)
        {
            _managedAdmins.Remove(Normalize(email));
            error = string.Empty;
            return true;
        }

        private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    }
}

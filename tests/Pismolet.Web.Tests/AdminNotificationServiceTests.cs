using Pismolet.Web.Application.Admin;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mail;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class AdminNotificationServiceTests
{
    private const string AdminEmail = "admin@example.test";
    private const string SecondAdminEmail = "second-admin@example.test";
    private const string ClientEmail = "client@example.test";
    private static readonly RequestMetadata Request = new("127.0.0.1", "admin-notification-tests");

    [Fact]
    public void Settings_are_disabled_by_default()
    {
        var settings = new InMemoryAdminNotificationSettingsRepository();

        var item = settings.Get(AdminEmail);

        Assert.False(item.UserRegistered);
        Assert.False(item.MailingCreated);
        Assert.False(item.MailingSubmittedToModeration);
        Assert.False(item.MailingPaid);
    }

    [Fact]
    public void Register_notifies_only_admins_with_enabled_user_registration_setting()
    {
        var users = new InMemoryUserRepository();
        var mailer = new InMemoryFakeMailer();
        var settings = new InMemoryAdminNotificationSettingsRepository();
        users.TryAdd(User(AdminEmail, "Admin"));
        users.TryAdd(User(SecondAdminEmail, "Second Admin"));
        settings.Save(AdminEmail, new AdminNotificationSettings(UserRegistered: true));
        var notifications = Notifications(users, mailer, settings, AdminEmail, SecondAdminEmail);
        var service = new UserAccountService(
            users,
            mailer,
            new InMemoryAuditLogger(),
            new InMemoryAdminMvpSettingsRepository(),
            new LegalEvidenceService(new InMemoryLegalEvidenceRepository()),
            notifications);

        var result = service.Register(new RegisterUserCommand(ClientEmail, "password123", "Client User", "+79990000001"), Request);

        Assert.True(result.Ok, result.Error);
        var adminNotifications = AdminNotifications(mailer);
        var item = Assert.Single(adminNotifications);
        Assert.Equal(AdminEmail, item.To);
        Assert.Contains("новый пользователь", item.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ClientEmail, item.TextBody);
    }

    [Fact]
    public void Mailing_created_notification_is_not_sent_for_admin_actor()
    {
        var users = new InMemoryUserRepository();
        var mailer = new InMemoryFakeMailer();
        var settings = new InMemoryAdminNotificationSettingsRepository();
        users.TryAdd(User(AdminEmail, "Admin"));
        settings.Save(AdminEmail, new AdminNotificationSettings(MailingCreated: true));
        var service = new MailingService(
            new InMemoryMailingRepository(),
            new InMemoryAuditLogger(),
            new EmailNormalizer(),
            Notifications(users, mailer, settings, AdminEmail));

        var result = service.CreateDraft(new CreateMailingCommand(AdminEmail, "Admin campaign"), Request);

        Assert.True(result.Ok, result.Error);
        Assert.Empty(AdminNotifications(mailer));
    }

    [Fact]
    public void Mailing_created_notification_is_sent_for_regular_user()
    {
        var users = new InMemoryUserRepository();
        var mailer = new InMemoryFakeMailer();
        var settings = new InMemoryAdminNotificationSettingsRepository();
        users.TryAdd(User(AdminEmail, "Admin"));
        users.TryAdd(User(ClientEmail, "Client"));
        settings.Save(AdminEmail, new AdminNotificationSettings(MailingCreated: true));
        var service = new MailingService(
            new InMemoryMailingRepository(),
            new InMemoryAuditLogger(),
            new EmailNormalizer(),
            Notifications(users, mailer, settings, AdminEmail));

        var result = service.CreateDraft(new CreateMailingCommand(ClientEmail, "Client campaign"), Request);

        Assert.True(result.Ok, result.Error);
        var item = Assert.Single(AdminNotifications(mailer));
        Assert.Equal(AdminEmail, item.To);
        Assert.Contains("создана новая рассылка", item.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Client campaign", item.TextBody);
    }

    [Fact]
    public void Start_checks_notification_is_sent_when_review_is_created()
    {
        var users = new InMemoryUserRepository();
        var mailings = new InMemoryMailingRepository();
        var payments = new InMemoryPaymentRepository();
        var mailer = new InMemoryFakeMailer();
        var settings = new InMemoryAdminNotificationSettingsRepository();
        users.TryAdd(User(AdminEmail, "Admin"));
        users.TryAdd(User(ClientEmail, "Client"));
        settings.Save(AdminEmail, new AdminNotificationSettings(MailingSubmittedToModeration: true));
        var mailing = ReadyMailing(ClientEmail, "Review campaign");
        mailings.TryAdd(mailing);
        var paidPayment = Payment.Create(mailing.Id, ClientEmail, 1, 0, PriceSettings.DefaultRub());
        payments.Save(paidPayment.MarkPaid(PaymentAttempt.Succeeded(paidPayment.Id, "paid-review", PaymentAttempt.RobokassaFakeProvider, "test")));
        var service = new MailingReviewService(
            mailings,
            payments,
            new InMemoryRiskCheckRepository(),
            new InMemoryModerationReviewRepository(),
            users,
            new RiskCheckService(),
            new EmailNormalizer(),
            new InMemoryAuditLogger(),
            Notifications(users, mailer, settings, AdminEmail));

        var result = service.StartChecks(ClientEmail, mailing.Id, Request);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.State?.Review);
        var item = Assert.Single(AdminNotifications(mailer));
        Assert.Contains("модерацию", item.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.State!.Review!.Id.ToString(), item.Link);
    }

    [Fact]
    public void Provider_payment_confirmation_sends_paid_notification_once()
    {
        var users = new InMemoryUserRepository();
        var mailings = new InMemoryMailingRepository();
        var payments = new InMemoryPaymentRepository();
        var prices = new InMemoryPriceSettingsRepository();
        var mailer = new InMemoryFakeMailer();
        var settings = new InMemoryAdminNotificationSettingsRepository();
        users.TryAdd(User(AdminEmail, "Admin"));
        users.TryAdd(User(ClientEmail, "Client"));
        settings.Save(AdminEmail, new AdminNotificationSettings(MailingPaid: true));
        var mailing = ReadyMailing(ClientEmail, "Paid campaign");
        mailings.TryAdd(mailing);
        var payment = Payment.Create(mailing.Id, ClientEmail, 1, 0, PriceSettings.DefaultRub());
        payment = payment.WithAttempt(PaymentAttempt.Pending(payment.Id, "paid-operation", PaymentAttempt.RobokassaFakeProvider));
        payments.Save(payment);
        var service = new MailingPaymentService(
            mailings,
            payments,
            prices,
            new MailingPricingService(prices),
            new FakeRobokassaPaymentProvider(),
            users,
            new EmailNormalizer(),
            new InMemoryAuditLogger(),
            Notifications(users, mailer, settings, AdminEmail));

        var first = service.ConfirmProviderPayment("paid-operation", Request, "result-url");
        var second = service.ConfirmProviderPayment("paid-operation", Request, "result-url-repeat");

        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        var item = Assert.Single(AdminNotifications(mailer));
        Assert.Contains("оплачена", item.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paid campaign", item.TextBody);
    }

    private static AdminNotificationService Notifications(
        InMemoryUserRepository users,
        InMemoryFakeMailer mailer,
        InMemoryAdminNotificationSettingsRepository settings,
        params string[] adminEmails) => new(
            users,
            new FakeAdminAccess(adminEmails),
            settings,
            mailer,
            new EmailNormalizer());

    private static IReadOnlyCollection<FakeMail> AdminNotifications(InMemoryFakeMailer mailer) => mailer
        .GetOutbox()
        .Where(item => item.Subject.StartsWith("Письмолёт:", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private static UserAccount User(string email, string displayName) => new(
        email.Trim().ToLowerInvariant(),
        "hash",
        displayName,
        $"token-{email}",
        EmailConfirmed: true,
        ClientProfile.NewClientDefault(),
        new List<Mailing>())
    {
        Phone = TestPhone(email)
    };

    private static string TestPhone(string email)
    {
        var checksum = 0;
        foreach (var ch in email.Trim().ToLowerInvariant())
        {
            checksum = ((checksum * 31) + ch) % 10_000_000;
        }

        return $"+7999{checksum:0000000}";
    }

    private static Mailing ReadyMailing(string ownerEmail, string subject)
    {
        var mailing = Mailing.Draft(ownerEmail, subject);
        var recipients = new[]
        {
            Recipient.Accepted("reader@example.test", "reader@example.test", rowNumber: 1)
        };
        var declaration = new MailingDeclaration(
            mailing.Id,
            ownerEmail,
            BaseSource.Customers,
            IsBaseLegalityConfirmed: true,
            IsAdvertisingConsentConfirmed: false,
            BaseDeclarationText.CurrentVersion,
            DateTimeOffset.UtcNow,
            Request.Ip,
            Request.UserAgent);
        return mailing
            .WithImportResult(new ImportStats(1, 1, 0, 0, 0), recipients)
            .WithDeclaration(declaration)
            .WithMessageDraft(MailingMessageDraft.Create("Sender", subject, "Body", MessageType.Transactional, DateTimeOffset.UtcNow));
    }

    private sealed class FakeAdminAccess(params string[] adminEmails) : IAdminAccessService
    {
        private readonly HashSet<string> _admins = adminEmails.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool IsAdminEmail(string? email) => !string.IsNullOrWhiteSpace(email) && _admins.Contains(Normalize(email));

        public bool IsConfigAdminEmail(string? email) => IsAdminEmail(email);

        public bool IsManagedAdminEmail(string? email) => false;

        public IReadOnlyCollection<string> ListManagedAdminEmails() => Array.Empty<string>();

        public void GrantAdmin(string email, string grantedByEmail) => _admins.Add(Normalize(email));

        public bool TryRevokeAdmin(string email, string revokedByEmail, out string error)
        {
            _admins.Remove(Normalize(email));
            error = string.Empty;
            return true;
        }

        private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    }
}

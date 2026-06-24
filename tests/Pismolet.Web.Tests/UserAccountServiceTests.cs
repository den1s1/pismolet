using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class UserAccountServiceTests
{
    private static readonly RequestMetadata Request = new("127.0.0.1", "unit-test");

    [Fact]
    public void Register_creates_user_profile_confirmation_token_and_mail()
    {
        var users = new InMemoryUserRepository();
        var mailer = new InMemoryFakeMailer();
        var audit = new InMemoryAuditLogger();
        var service = new UserAccountService(users, mailer, audit, new InMemoryAdminMvpSettingsRepository(), LegalEvidence());

        var result = service.Register(new RegisterUserCommand("CLIENT@EXAMPLE.COM", "password123", "Клиент"), Request);

        Assert.True(result.Ok);
        Assert.NotNull(result.ConfirmLink);

        var user = users.GetByEmail("client@example.com");
        Assert.NotNull(user);
        Assert.Equal("Клиент", user.DisplayName);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(1000, user.Profile.DailySendLimit);
        Assert.True(user.Profile.PremoderationRequired);
        Assert.Single(user.Mailings);
        Assert.Single(mailer.GetOutbox());
        Assert.Contains(audit.GetRecords(), record => record.EventType == "registration");
    }

    [Fact]
    public void Authenticate_rejects_user_before_email_confirmation()
    {
        var users = new InMemoryUserRepository();
        var service = new UserAccountService(users, new InMemoryFakeMailer(), new InMemoryAuditLogger(), new InMemoryAdminMvpSettingsRepository(), LegalEvidence());

        service.Register(new RegisterUserCommand("client@example.com", "password123", "Клиент"), Request);

        var authenticated = service.Authenticate(new LoginUserCommand("client@example.com", "password123"), Request);

        Assert.Null(authenticated);
    }

    [Fact]
    public void ConfirmEmail_confirms_user_and_allows_login()
    {
        var users = new InMemoryUserRepository();
        var audit = new InMemoryAuditLogger();
        var legalEvidenceRepository = new InMemoryLegalEvidenceRepository();
        var service = new UserAccountService(
            users,
            new InMemoryFakeMailer(),
            audit,
            new InMemoryAdminMvpSettingsRepository(),
            new LegalEvidenceService(legalEvidenceRepository));
        var result = service.Register(new RegisterUserCommand("client@example.com", "password123", "Клиент"), Request);
        var token = result.ConfirmLink!.Split("token=")[1];

        Assert.True(service.ConfirmEmail(token, Request));

        var authenticated = service.Authenticate(new LoginUserCommand("client@example.com", "password123"), Request);
        Assert.NotNull(authenticated);
        Assert.True(authenticated.EmailConfirmed);
        Assert.Contains(audit.GetRecords(), record => record.EventType == "email_confirmed");
        Assert.Contains(audit.GetRecords(), record => record.EventType == "login");
        Assert.Contains(legalEvidenceRepository.ListEventsForClient("client@example.com"), record => record.EventType == "client_email_confirmed");
    }

    private static LegalEvidenceService LegalEvidence() => new(new InMemoryLegalEvidenceRepository());
}

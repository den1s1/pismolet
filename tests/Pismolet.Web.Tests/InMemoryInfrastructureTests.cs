using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;
using Pismolet.Web.Domain.Audit;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class InMemoryInfrastructureTests
{
    [Fact]
    public void User_repository_adds_user_and_prevents_duplicate_email()
    {
        var repository = new InMemoryUserRepository();
        var user = CreateUser("client@example.com");

        Assert.True(repository.TryAdd(user));
        Assert.False(repository.TryAdd(user));
        Assert.True(repository.Exists("client@example.com"));
        Assert.Equal(user, repository.GetByEmail("client@example.com"));
    }

    [Fact]
    public void User_repository_finds_user_by_confirmation_token()
    {
        var repository = new InMemoryUserRepository();
        var user = CreateUser("client@example.com");

        repository.TryAdd(user);

        Assert.Equal(user, repository.FindByConfirmationToken("token-client@example.com"));
    }

    [Fact]
    public void Fake_mailer_keeps_confirmation_mail_in_outbox()
    {
        var mailer = new InMemoryFakeMailer();

        mailer.SendConfirmation("client@example.com", "Подтверждение email", "/account/confirm-email?token=abc");

        var message = Assert.Single(mailer.GetOutbox());
        Assert.Equal("client@example.com", message.To);
        Assert.Contains("confirm-email", message.Link);
    }

    [Fact]
    public void Audit_logger_keeps_written_records()
    {
        var audit = new InMemoryAuditLogger();

        audit.Write(new AuditRecord(DateTimeOffset.UtcNow, "client@example.com", "registration", "127.0.0.1", "test", "{}"));

        var record = Assert.Single(audit.GetRecords());
        Assert.Equal("registration", record.EventType);
    }

    private static UserAccount CreateUser(string email) => new(
        Email: email,
        PasswordHash: "dev:password123",
        DisplayName: "Client",
        ConfirmationToken: "token-" + email,
        EmailConfirmed: false,
        Profile: ClientProfile.NewClientDefault(),
        Mailings: [Mailing.Draft("Тестовая рассылка")]);
}

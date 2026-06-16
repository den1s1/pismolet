using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailingServiceTests
{
    private static readonly RequestMetadata Request = new("127.0.0.1", "unit-test");

    [Fact]
    public void CreateDraft_creates_mailing_for_user_and_writes_audit()
    {
        var repository = new InMemoryMailingRepository();
        var audit = new InMemoryAuditLogger();
        var service = new MailingService(repository, audit, new EmailNormalizer());

        var result = service.CreateDraft(new CreateMailingCommand("CLIENT@EXAMPLE.COM", "Новости клуба"), Request);

        Assert.True(result.Ok);
        Assert.NotNull(result.Mailing);
        Assert.Equal("client@example.com", result.Mailing.OwnerEmail);
        Assert.Equal("Новости клуба", result.Mailing.Subject);
        Assert.Contains(audit.GetRecords(), record => record.EventType == "mailing_created");
    }

    [Fact]
    public void CreateDraft_rejects_empty_subject()
    {
        var service = new MailingService(new InMemoryMailingRepository(), new InMemoryAuditLogger(), new EmailNormalizer());

        var result = service.CreateDraft(new CreateMailingCommand("client@example.com", "   "), Request);

        Assert.False(result.Ok);
        Assert.Equal("Укажите название рассылки.", result.Error);
    }

    [Fact]
    public void ListForOwner_returns_only_user_mailings()
    {
        var repository = new InMemoryMailingRepository();
        var service = new MailingService(repository, new InMemoryAuditLogger(), new EmailNormalizer());

        service.CreateDraft(new CreateMailingCommand("first@example.com", "Первая"), Request);
        service.CreateDraft(new CreateMailingCommand("second@example.com", "Вторая"), Request);

        var result = service.ListForOwner("FIRST@example.com");

        var mailing = Assert.Single(result);
        Assert.Equal("first@example.com", mailing.OwnerEmail);
    }
}

using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class Sprint3ServiceTests
{
    [Fact]
    public void Message_rendering_adds_unsubscribe_reason_and_service_identifier()
    {
        var mailing = Mailing
            .Draft("client@test.local", "Новости")
            .WithImportResult(new ImportStats(1, 1, 0, 0, 0), new[] { Recipient.Accepted("lead@test.local", "lead@test.local") })
            .WithDeclaration(new MailingDeclaration(
                Guid.Empty,
                "client@test.local",
                BaseSource.Customers,
                true,
                true,
                BaseDeclarationText.CurrentVersion,
                DateTimeOffset.UtcNow,
                "127.0.0.1",
                "unit-test"))
            .WithMessageDraft(MailingMessageDraft.Create("Письмолёт", "Новости", "Текст письма", MessageType.Transactional, DateTimeOffset.UtcNow));

        var preview = new MessageRenderingService().RenderPreview(mailing);

        Assert.Contains("/unsubscribe/", preview.PlainText);
        Assert.Contains("Почему вы получили это письмо", preview.PlainText);
        Assert.Contains("Служебный идентификатор рассылки", preview.PlainText);
    }

    [Fact]
    public void Message_requires_sender_subject_and_body()
    {
        var ex = Assert.Throws<ArgumentException>(() => MailingMessageDraft.Create("", "", "", MessageType.Transactional, DateTimeOffset.UtcNow));

        Assert.Contains("имя отправителя", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Declaration_saves_current_text_version()
    {
        var repository = new Pismolet.Web.Infrastructure.Persistence.InMemoryMailingRepository();
        var service = new MailingDeclarationService(repository, new EmailNormalizer(), new Pismolet.Web.Infrastructure.Audit.InMemoryAuditLogger());
        var mailing = Mailing
            .Draft("client@test.local", "Новости")
            .WithImportResult(new ImportStats(1, 1, 0, 0, 0), new[] { Recipient.Accepted("lead@test.local", "lead@test.local") });
        repository.TryAdd(mailing);

        var result = service.Confirm(new ConfirmMailingDeclarationCommand(
            mailing.OwnerEmail,
            mailing.Id,
            BaseSource.FormSubscribers,
            true,
            false,
            MessageType.Transactional,
            new Pismolet.Web.Application.Common.RequestMetadata("127.0.0.1", "unit-test")));

        Assert.True(result.Ok);
        Assert.Equal(BaseDeclarationText.CurrentVersion, repository.Get(mailing.Id)!.Declaration!.DeclarationVersion);
    }
}

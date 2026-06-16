using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class Sprint3ServiceTests
{
    private static readonly RequestMetadata Request = new("127.0.0.1", "unit-test");

    [Fact]
    public void Declaration_requires_base_legality_confirmation()
    {
        var (repository, service, mailing) = CreateDeclarationServiceWithImportedMailing();

        var result = service.Confirm(new ConfirmMailingDeclarationCommand(
            mailing.OwnerEmail,
            mailing.Id,
            BaseSource.Customers,
            false,
            false,
            MessageType.Transactional,
            Request));

        Assert.False(result.Ok);
        Assert.Null(repository.Get(mailing.Id)!.Declaration);
    }

    [Fact]
    public void Declaration_requires_advertising_consent_for_advertising_message()
    {
        var (_, service, mailing) = CreateDeclarationServiceWithImportedMailing();

        var result = service.Confirm(new ConfirmMailingDeclarationCommand(
            mailing.OwnerEmail,
            mailing.Id,
            BaseSource.Customers,
            true,
            false,
            MessageType.Advertising,
            Request));

        Assert.False(result.Ok);
    }

    [Fact]
    public void Declaration_saves_current_text_version()
    {
        var (repository, service, mailing) = CreateDeclarationServiceWithImportedMailing();

        var result = service.Confirm(new ConfirmMailingDeclarationCommand(
            mailing.OwnerEmail,
            mailing.Id,
            BaseSource.FormSubscribers,
            true,
            false,
            MessageType.Transactional,
            Request));

        Assert.True(result.Ok);
        Assert.Equal(BaseDeclarationText.CurrentVersion, repository.Get(mailing.Id)!.Declaration!.DeclarationVersion);
    }

    [Fact]
    public void Declaration_cannot_be_confirmed_before_import()
    {
        var repository = new InMemoryMailingRepository();
        var audit = new InMemoryAuditLogger();
        var service = new MailingDeclarationService(repository, new EmailNormalizer(), audit);
        var mailing = Mailing.Draft("client@example.com", "Новости");
        repository.TryAdd(mailing);

        var result = service.Confirm(new ConfirmMailingDeclarationCommand(
            mailing.OwnerEmail,
            mailing.Id,
            BaseSource.Customers,
            true,
            false,
            MessageType.Transactional,
            Request));

        Assert.False(result.Ok);
        Assert.Null(repository.Get(mailing.Id)!.Declaration);
    }

    [Fact]
    public void Message_cannot_be_saved_without_declaration()
    {
        var repository = new InMemoryMailingRepository();
        var service = new MailingMessageService(repository, new EmailNormalizer(), new InMemoryAuditLogger());
        var mailing = ImportedMailing();
        repository.TryAdd(mailing);

        var result = service.Save(new SaveMailingMessageCommand(
            mailing.OwnerEmail,
            mailing.Id,
            "Письмолёт",
            "Новости",
            "Текст письма",
            MessageType.Transactional,
            Request));

        Assert.False(result.Ok);
    }

    [Fact]
    public void Advertising_message_cannot_be_saved_without_advertising_consent()
    {
        var repository = new InMemoryMailingRepository();
        var service = new MailingMessageService(repository, new EmailNormalizer(), new InMemoryAuditLogger());
        var mailing = ImportedMailing().WithDeclaration(new MailingDeclaration(
            Guid.Empty,
            "client@example.com",
            BaseSource.Customers,
            true,
            false,
            BaseDeclarationText.CurrentVersion,
            DateTimeOffset.UtcNow,
            "127.0.0.1",
            "unit-test"));
        repository.TryAdd(mailing);

        var result = service.Save(new SaveMailingMessageCommand(
            mailing.OwnerEmail,
            mailing.Id,
            "Письмолёт",
            "Новости",
            "Текст письма",
            MessageType.Advertising,
            Request));

        Assert.False(result.Ok);
    }

    [Fact]
    public void Message_requires_sender_subject_and_body()
    {
        var ex = Assert.Throws<ArgumentException>(() => MailingMessageDraft.Create("", "", "", MessageType.Transactional, DateTimeOffset.UtcNow));
        Assert.Contains("имя отправителя", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Message_rendering_adds_unsubscribe_link()
    {
        var preview = RenderPreparedMailing();

        Assert.Contains("/unsubscribe/", preview.PlainText);
    }

    [Fact]
    public void Message_rendering_adds_reason_block()
    {
        var preview = RenderPreparedMailing();

        Assert.Contains("Почему вы получили это письмо", preview.PlainText);
    }

    [Fact]
    public void Message_rendering_adds_service_identifier()
    {
        var preview = RenderPreparedMailing();

        Assert.Contains("Служебный идентификатор рассылки", preview.PlainText);
    }

    [Fact]
    public void Unsubscribe_token_is_generated_for_recipient()
    {
        var service = new DevUnsubscribeTokenService();

        var first = service.Generate(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "USER@example.com");
        var second = service.Generate(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "user@example.com");

        Assert.Equal(first, second);
        Assert.False(string.IsNullOrWhiteSpace(first));
    }

    private static (InMemoryMailingRepository Repository, MailingDeclarationService Service, Mailing Mailing) CreateDeclarationServiceWithImportedMailing()
    {
        var repository = new InMemoryMailingRepository();
        var service = new MailingDeclarationService(repository, new EmailNormalizer(), new InMemoryAuditLogger());
        var mailing = ImportedMailing();
        repository.TryAdd(mailing);
        return (repository, service, mailing);
    }

    private static Mailing ImportedMailing() => Mailing
        .Draft("client@example.com", "Новости")
        .WithImportResult(new ImportStats(1, 1, 0, 0, 0), new[] { Recipient.Accepted("lead@example.com", "lead@example.com") });

    private static RenderedMessagePreview RenderPreparedMailing()
    {
        var mailing = ImportedMailing()
            .WithDeclaration(new MailingDeclaration(
                Guid.Empty,
                "client@example.com",
                BaseSource.Customers,
                true,
                true,
                BaseDeclarationText.CurrentVersion,
                DateTimeOffset.UtcNow,
                "127.0.0.1",
                "unit-test"))
            .WithMessageDraft(MailingMessageDraft.Create("Письмолёт", "Новости", "Текст письма", MessageType.Transactional, DateTimeOffset.UtcNow));

        return new MessageRenderingService(new DevUnsubscribeTokenService()).RenderPreview(mailing);
    }
}

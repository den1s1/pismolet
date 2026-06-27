using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Infrastructure.Persistence;

namespace Pismolet.Web.Tests;

public sealed class EfMailingRepositoryTests
{
    private const string OwnerEmail = "ef-mailing-owner@example.test";

    [Fact]
    public void Ef_mailing_repository_roundtrips_message_attachments()
    {
        using var db = CreateContext();
        var repository = new EfMailingRepository(db);
        var mailing = ReadyMailing(Guid.NewGuid()).WithMessageDraft(MailingMessageDraft.Create(
            "Sender",
            "Subject",
            "Body",
            MessageType.Transactional,
            DateTimeOffset.Parse("2026-06-28T08:00:00Z"),
            new[]
            {
                MailingAttachment.Create("invoice.pdf", "application/pdf", new byte[] { 1, 2, 3, 4 })
            }));

        repository.Update(mailing);
        db.ChangeTracker.Clear();

        var saved = repository.GetForOwner(mailing.Id, OwnerEmail);

        Assert.NotNull(saved?.MessageDraft);
        var attachment = Assert.Single(saved.MessageDraft.Attachments);
        Assert.Equal("invoice.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, attachment.Content);
        Assert.Equal(4, attachment.Size);
    }

    [Fact]
    public void Mailing_message_service_keeps_existing_ef_attachments_when_no_new_files_are_uploaded()
    {
        using var db = CreateContext();
        var repository = new EfMailingRepository(db);
        var mailing = ReadyMailing(Guid.NewGuid()).WithMessageDraft(MailingMessageDraft.Create(
            "Sender",
            "Old subject",
            "Old body",
            MessageType.Transactional,
            DateTimeOffset.Parse("2026-06-28T08:10:00Z"),
            new[]
            {
                MailingAttachment.Create("terms.txt", "text/plain", new byte[] { 10, 20, 30 })
            }));
        repository.Update(mailing);
        db.ChangeTracker.Clear();
        var service = new MailingMessageService(repository, new EmailNormalizer(), new InMemoryAuditLogger());

        var result = service.Save(new SaveMailingMessageCommand(
            OwnerEmail,
            mailing.Id,
            "Updated sender",
            "Updated subject",
            "Updated body",
            MessageType.Transactional,
            new RequestMetadata("127.0.0.1", "ef-mailing-repository-tests")));

        Assert.True(result.Ok, result.Error);
        db.ChangeTracker.Clear();
        var saved = repository.GetForOwner(mailing.Id, OwnerEmail);

        Assert.NotNull(saved?.MessageDraft);
        Assert.Equal("Updated subject", saved.MessageDraft.Subject);
        var attachment = Assert.Single(saved.MessageDraft.Attachments);
        Assert.Equal("terms.txt", attachment.FileName);
        Assert.Equal("text/plain", attachment.ContentType);
        Assert.Equal(new byte[] { 10, 20, 30 }, attachment.Content);
    }

    private static Mailing ReadyMailing(Guid id)
    {
        var mailing = Mailing.Draft(OwnerEmail, "EF attachment campaign") with { Id = id };
        var recipients = new[]
        {
            Recipient.Accepted("reader@example.test", "reader@example.test", rowNumber: 1)
        };
        var declaration = new MailingDeclaration(
            id,
            OwnerEmail,
            BaseSource.Customers,
            IsBaseLegalityConfirmed: true,
            IsAdvertisingConsentConfirmed: false,
            BaseDeclarationText.CurrentVersion,
            DateTimeOffset.Parse("2026-06-28T08:00:00Z"),
            "127.0.0.1",
            "ef-mailing-repository-tests");

        return mailing
            .WithImportResult(new ImportStats(1, 1, 0, 0, 0), recipients)
            .WithDeclaration(declaration);
    }

    private static PismoletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PismoletDbContext>()
            .UseSqlite($"Data Source=file:ef-mailing-repository-{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        var db = new PismoletDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}

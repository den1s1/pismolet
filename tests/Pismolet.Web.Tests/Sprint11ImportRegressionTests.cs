using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;

namespace Pismolet.Web.Tests;

public sealed class Sprint11ImportRegressionTests
{
    [Fact]
    public async Task Empty_csv_returns_clear_russian_error()
    {
        var service = CreateService(out var mailings, out _);
        var mailing = AddMailing(mailings);

        var result = await service.ImportAsync(Command(mailing.Id, "empty.csv", string.Empty));

        Assert.False(result.Ok);
        Assert.Equal("Файл пустой.", result.Error);
    }

    [Fact]
    public async Task Csv_without_email_column_returns_clear_russian_error()
    {
        var service = CreateService(out var mailings, out _);
        var mailing = AddMailing(mailings);

        var result = await service.ImportAsync(Command(mailing.Id, "wrong.csv", "name\nIvan"));

        Assert.False(result.Ok);
        Assert.Equal("В файле должна быть колонка email.", result.Error);
    }

    [Fact]
    public async Task Csv_with_only_invalid_rows_has_no_accepted_recipients_but_keeps_rows_for_management()
    {
        var service = CreateService(out var mailings, out _);
        var mailing = AddMailing(mailings);

        var result = await service.ImportAsync(Command(mailing.Id, "bad.csv", "email\nwrong-email\nwrong-email"));

        Assert.True(result.Ok);
        Assert.NotNull(result.Mailing);
        Assert.Equal(0, result.Stats.Accepted);
        Assert.Equal(2, result.Stats.Invalid);
        Assert.Empty(result.Mailing!.Recipients.Where(x => x.Status == RecipientStatus.Accepted));
        Assert.Equal(2, result.Mailing.Recipients.Count(x => x.Status == RecipientStatus.Invalid));
    }

    [Fact]
    public async Task Global_and_client_suppressed_recipients_are_reported_and_kept_for_management()
    {
        var service = CreateService(out var mailings, out var state);
        var mailing = AddMailing(mailings);
        state.GlobalSuppressions.Add("unsubscribed@example.test");
        state.ClientSuppressions.AddOrUpdate(ClientSuppression.FromHardBounce("owner@example.test", "hard@example.test", mailing.Id, "provider-message-1"));

        var csv = "email\nok@example.test\nunsubscribed@example.test\nhard@example.test";
        var result = await service.ImportAsync(Command(mailing.Id, "mixed.csv", csv));

        Assert.True(result.Ok);
        Assert.Equal(1, result.Stats.Accepted);
        Assert.Equal(1, result.Stats.GloballySuppressed);
        Assert.Equal(1, result.Stats.ClientSuppressed);
        var accepted = Assert.Single(result.Mailing!.Recipients.Where(x => x.Status == RecipientStatus.Accepted));
        Assert.Equal("ok@example.test", accepted.Email);
        Assert.Contains(result.Mailing.Recipients, x => x.Email == "unsubscribed@example.test" && x.Status == RecipientStatus.GloballySuppressed);
        Assert.Contains(result.Mailing.Recipients, x => x.Email == "hard@example.test" && x.Status == RecipientStatus.ClientSuppressed);
    }

    private static RecipientImportService CreateService(out InMemoryMailingRepository mailings, out TestImportState state)
    {
        mailings = new InMemoryMailingRepository();
        state = new TestImportState(new InMemoryGlobalSuppressionRepository(), new InMemoryClientSuppressionRepository(), new InMemorySendEventRepository());
        return new RecipientImportService(
            mailings,
            state.GlobalSuppressions,
            state.ClientSuppressions,
            state.SendEvents,
            new EmailNormalizer(),
            new EmailSyntaxValidator(),
            new InMemoryAuditLogger());
    }

    private static Mailing AddMailing(InMemoryMailingRepository mailings)
    {
        var mailing = Mailing.Draft("owner@example.test", "Sprint 11 import test");
        Assert.True(mailings.TryAdd(mailing));
        return mailing;
    }

    private static ImportRecipientsCommand Command(Guid mailingId, string fileName, string content) => new(
        "owner@example.test",
        mailingId,
        fileName,
        new MemoryStream(Encoding.UTF8.GetBytes(content)),
        new RequestMetadata("127.0.0.1", "tests"));

    private sealed record TestImportState(
        InMemoryGlobalSuppressionRepository GlobalSuppressions,
        InMemoryClientSuppressionRepository ClientSuppressions,
        InMemorySendEventRepository SendEvents);
}

using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class Sprint11StateMachineTests
{
    [Fact]
    public void Approved_mailing_can_start_sending()
    {
        var mailing = Mailing.Draft("owner@example.test", "Demo").WithStatus(MailingStatus.Approved);

        var allowed = MailingStateMachine.CanStartSending(mailing, isClientBlocked: false, out var error);

        Assert.True(allowed);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(MailingStatus.Draft)]
    [InlineData(MailingStatus.MessagePrepared)]
    [InlineData(MailingStatus.PaymentPending)]
    [InlineData(MailingStatus.Paid)]
    [InlineData(MailingStatus.PendingChecks)]
    [InlineData(MailingStatus.ReviewRequired)]
    [InlineData(MailingStatus.Sending)]
    [InlineData(MailingStatus.Sent)]
    [InlineData(MailingStatus.Rejected)]
    public void Non_approved_mailing_cannot_start_sending(MailingStatus status)
    {
        var mailing = Mailing.Draft("owner@example.test", "Demo").WithStatus(status);

        var allowed = MailingStateMachine.CanStartSending(mailing, isClientBlocked: false, out var error);

        Assert.False(allowed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Blocked_client_cannot_start_even_approved_mailing()
    {
        var mailing = Mailing.Draft("owner@example.test", "Demo").WithStatus(MailingStatus.Approved);

        var allowed = MailingStateMachine.CanStartSending(mailing, isClientBlocked: true, out var error);

        Assert.False(allowed);
        Assert.Equal("Клиент заблокирован администратором.", error);
    }

    [Fact]
    public void Blocked_mailing_has_no_available_next_action()
    {
        var mailing = Mailing.Draft("owner@example.test", "Demo").WithStatus(MailingStatus.Blocked);

        var next = MailingStateMachine.GetNextAction(mailing);

        Assert.False(next.IsAvailable);
        Assert.Null(next.Url);
        Assert.Contains("заблокирована", next.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Imported_mailing_without_accepted_recipients_sends_user_back_to_import()
    {
        var mailing = Mailing.Draft("owner@example.test", "Demo") with
        {
            Status = MailingStatus.RecipientsImported,
            StatusRu = MailingStatus.RecipientsImported.ToRu(),
            LastImportStats = new ImportStats(TotalRows: 2, Accepted: 0, Duplicates: 1, Invalid: 1, GloballySuppressed: 0)
        };

        var next = MailingStateMachine.GetNextAction(mailing);

        Assert.Equal("Загрузить другой список", next.Label);
        Assert.Contains("/recipients", next.Url);
    }
}

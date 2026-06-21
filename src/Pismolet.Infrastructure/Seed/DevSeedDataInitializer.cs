using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Infrastructure.Seed;

public sealed class DevSeedDataInitializer(
    IUserRepository users,
    IMailingRepository mailings,
    IGlobalSuppressionRepository suppressions)
{
    public const string DemoEmail = "demo@pismolet.local";
    public const string DemoPassword = "password123";

    public void Seed()
    {
        suppressions.Add("unsubscribed@example.test");

        if (users.Exists(DemoEmail))
        {
            return;
        }

        var recipients = new[]
        {
            Recipient.Accepted("ok@example.test", "ok@example.test"),
            Recipient.Accepted("please-fail@example.test", "please-fail@example.test"),
            Recipient.Accepted("temp@example.test", "temp@example.test"),
            Recipient.Accepted("hard-bounce@example.test", "hard-bounce@example.test"),
            Recipient.Accepted("complaint@example.test", "complaint@example.test")
        };

        var mailing = Mailing
            .Draft(DemoEmail, "Демо-рассылка Sprint 11")
            .WithImportResult(
                ImportBatch.Completed(
                    Guid.Empty,
                    "demo_recipients.csv",
                    ImportSourceFormat.Csv,
                    new ImportStats(8, 5, 1, 1, 1),
                    new[]
                    {
                        new RecipientImportIssue(7, "not-an-email", "Невалидный email"),
                        new RecipientImportIssue(8, "ok@example.test", "Дубликат адреса"),
                        new RecipientImportIssue(6, "unsubscribed@example.test", "Адрес уже отписан от рассылок сервиса")
                    }),
                recipients);

        var user = new UserAccount(
            DemoEmail,
            "dev:" + DemoPassword,
            "Демо-пользователь",
            Guid.NewGuid().ToString("N"),
            true,
            ClientProfile.NewClientDefault(),
            new List<Mailing> { mailing });

        users.TryAdd(user);
        mailings.TryAdd(mailing);
    }
}

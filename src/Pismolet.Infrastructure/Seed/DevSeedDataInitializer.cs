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
        if (users.Exists(DemoEmail))
        {
            return;
        }

        var mailing = Mailing
            .Draft(DemoEmail, "Демо-рассылка")
            .WithImportResult(
                ImportBatch.Completed(
                    Guid.Empty,
                    "demo.csv",
                    ImportSourceFormat.Csv,
                    new ImportStats(3, 2, 0, 1, 0),
                    new[] { new RecipientImportIssue(4, "wrong-email", "Невалидный email") }),
                new[]
                {
                    Recipient.Accepted("first@example.com", "first@example.com"),
                    Recipient.Accepted("second@example.com", "second@example.com")
                });

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
        suppressions.Add("unsubscribed@example.com");
    }
}

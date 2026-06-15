namespace Pismolet.Web.Domain.Users;

public sealed record ClientProfile(
    string Status,
    int DailySendLimit,
    int TotalSendLimit,
    bool PremoderationRequired)
{
    public static ClientProfile NewClientDefault() => new(
        Status: "active",
        DailySendLimit: 1000,
        TotalSendLimit: 10000,
        PremoderationRequired: true);
}

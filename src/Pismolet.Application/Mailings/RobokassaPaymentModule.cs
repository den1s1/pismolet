using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pismolet.Web.Application.Mailings;

public sealed record RobokassaPaymentOptions(
    string MerchantLogin,
    string Password1,
    string Password2,
    bool IsTest,
    string PaymentUrl)
{
    public static RobokassaPaymentOptions DevelopmentDefault { get; } = new(
        "pismolet-demo",
        "dev-robokassa-password-1",
        "dev-robokassa-password-2",
        true,
        "/payments/robokassa/fake/checkout");
}

public static class RobokassaPaymentModule
{
    public static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string BuildStartSignature(
        string merchantLogin,
        string outSum,
        string invId,
        string password1,
        IReadOnlyDictionary<string, string> shpParameters) =>
        Md5Hex(BuildSignatureBase(new[] { merchantLogin, outSum, invId, password1 }, shpParameters));

    public static string BuildResultSignature(
        string outSum,
        string invId,
        string password2,
        IReadOnlyDictionary<string, string> shpParameters) =>
        Md5Hex(BuildSignatureBase(new[] { outSum, invId, password2 }, shpParameters));

    public static string BuildSuccessSignature(
        string outSum,
        string invId,
        string password1,
        IReadOnlyDictionary<string, string> shpParameters) =>
        Md5Hex(BuildSignatureBase(new[] { outSum, invId, password1 }, shpParameters));

    public static bool Verify(string actualSignature, string expectedSignature) =>
        !string.IsNullOrWhiteSpace(actualSignature)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualSignature.Trim().ToUpperInvariant()),
            Encoding.UTF8.GetBytes(expectedSignature.Trim().ToUpperInvariant()));

    public static IReadOnlyDictionary<string, string> ShpMailing(Guid mailingId) =>
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shp_mailingId"] = mailingId.ToString("N")
        };

    public static string BuildInvId(Guid paymentId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(paymentId.ToString("N")));
        var value = BitConverter.ToUInt64(bytes, 0) % 9_000_000_000_000_000UL;
        return Math.Max(1UL, value).ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildSignatureBase(IEnumerable<string> parts, IReadOnlyDictionary<string, string> shpParameters)
    {
        var orderedShp = shpParameters
            .Where(parameter => parameter.Key.StartsWith("Shp_", StringComparison.Ordinal))
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter => $"{parameter.Key}={parameter.Value}");

        return string.Join(':', parts.Concat(orderedShp));
    }

    private static string Md5Hex(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}

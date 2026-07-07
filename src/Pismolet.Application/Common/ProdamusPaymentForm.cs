using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pismolet.Web.Application.Common;

public static class ProdamusPaymentForm
{
    public const string ProviderName = "Prodamus";

    private static readonly string[] CheckFieldNames = { "signature", "sign", "Signature" };
    private static readonly string[] OperationIdFieldNames = { "order_id", "order_num", "order", "payment_id", "paymentId", "id" };
    private static readonly string[] AmountFieldNames = { "amount", "sum", "payment_amount", "paid_amount", "order_sum" };
    private static readonly string[] StatusFieldNames = { "payment_status", "status", "order_status", "state" };

    public static string BuildOrderId(Guid paymentId) => $"pm-{paymentId:N}";

    public static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static Dictionary<string, string> BuildStartFields(Guid mailingId, string publicId, string ownerEmail, string operationId, int acceptedRecipients, int excludedRecipients, decimal pricePerRecipient, decimal totalAmount, string currency, ProdamusOptions options, string successUrl, string failUrl, string resultUrl)
    {
        var amount = FormatAmount(totalAmount);
        var description = $"{options.ServiceName}. Рассылка {publicId}: {acceptedRecipients} писем × {pricePerRecipient:0.##} ₽; исключено {excludedRecipients}.";
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["do"] = "pay",
            ["order_id"] = operationId,
            ["customer_email"] = ownerEmail,
            ["amount"] = amount,
            ["currency"] = currency,
            ["description"] = description,
            ["products[0][name]"] = options.ServiceName,
            ["products[0][price]"] = amount,
            ["products[0][quantity]"] = "1",
            ["metadata[mailing_id]"] = mailingId.ToString("D"),
            ["metadata[accepted_recipients]"] = acceptedRecipients.ToString(CultureInfo.InvariantCulture),
            ["metadata[excluded_recipients]"] = excludedRecipients.ToString(CultureInfo.InvariantCulture),
            ["metadata[price_per_recipient]"] = pricePerRecipient.ToString("0.##", CultureInfo.InvariantCulture),
            ["url_success"] = successUrl,
            ["url_fail"] = failUrl,
            ["url_result"] = resultUrl,
            ["url_notification"] = resultUrl
        };

        if (options.IsTest) fields["test"] = "1";
        if (options.HasCallbackCheckValue) fields["signature"] = BuildCheck(fields, options.CallbackCheckValue);
        return fields;
    }

    public static string BuildCheck(IReadOnlyDictionary<string, string> fields, string checkValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalString(fields) + "|" + checkValue));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool VerifyCheck(IReadOnlyDictionary<string, string> fields, string expected, string checkValue) =>
        !string.IsNullOrWhiteSpace(expected) && string.Equals(BuildCheck(fields, checkValue), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string? ExtractCheck(IReadOnlyDictionary<string, string> fields) => FirstNonEmpty(fields, CheckFieldNames);

    public static bool TryGetOperationId(IReadOnlyDictionary<string, string> fields, out string operationId)
    {
        operationId = FirstNonEmpty(fields, OperationIdFieldNames) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(operationId);
    }

    public static bool TryGetAmount(IReadOnlyDictionary<string, string> fields, out decimal amount)
    {
        foreach (var name in AmountFieldNames)
        {
            if (fields.TryGetValue(name, out var value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)) return true;
        }
        amount = 0m;
        return false;
    }

    public static bool IsPaidCallback(IReadOnlyDictionary<string, string> fields)
    {
        var status = FirstNonEmpty(fields, StatusFieldNames);
        if (string.IsNullOrWhiteSpace(status)) return true;
        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "success" or "succeeded" or "paid" or "completed" or "approved" or "ok" or "1";
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    private static string CanonicalString(IReadOnlyDictionary<string, string> fields) => string.Join('&', fields
        .Where(field => !CheckFieldNames.Contains(field.Key, StringComparer.OrdinalIgnoreCase))
        .Where(field => !string.IsNullOrWhiteSpace(field.Value))
        .OrderBy(field => field.Key, StringComparer.Ordinal)
        .Select(field => $"{field.Key}={field.Value}"));
}

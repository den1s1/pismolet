using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Pismolet.Web.Application.Common;

public static class ProdamusPaymentForm
{
    public const string ProviderName = "Prodamus";

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.Default };
    private static readonly string[] CheckFieldNames = { "signature", "sign", "Signature" };
    private static readonly string[] OperationIdFieldNames = { "order_id", "order_num", "order", "payment_id", "paymentId", "id" };
    private static readonly string[] AmountFieldNames = { "order_sum", "sum", "amount", "payment_amount", "paid_amount", "products[0][price]" };
    private static readonly string[] StatusFieldNames = { "payment_status", "status", "order_status", "state" };

    public static string BuildOrderId(Guid paymentId) => $"pm-{paymentId:N}";

    public static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static Dictionary<string, string> BuildStartFields(Guid mailingId, string publicId, string ownerEmail, string operationId, int acceptedRecipients, int excludedRecipients, decimal pricePerRecipient, decimal totalAmount, string currency, ProdamusOptions options, string successUrl, string failUrl, string resultUrl)
    {
        var amount = FormatAmount(totalAmount);
        var unitPrice = pricePerRecipient.ToString("0.##", CultureInfo.InvariantCulture);
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "rub" : currency.Trim().ToLowerInvariant();
        var customerExtra = $"Рассылка {publicId}: {acceptedRecipients} писем × {unitPrice} ₽; исключено {excludedRecipients}.";
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["do"] = "pay",
            ["order_id"] = operationId,
            ["customer_email"] = ownerEmail,
            ["customer_extra"] = customerExtra,
            ["currency"] = normalizedCurrency,
            ["products[0][name]"] = options.ServiceName,
            ["products[0][price]"] = amount,
            ["products[0][quantity]"] = "1",
            ["urlReturn"] = failUrl,
            ["urlSuccess"] = successUrl,
            ["urlNotification"] = resultUrl,
            ["_param_mailing_id"] = mailingId.ToString("D"),
            ["_param_public_id"] = publicId,
            ["_param_accepted_recipients"] = acceptedRecipients.ToString(CultureInfo.InvariantCulture),
            ["_param_excluded_recipients"] = excludedRecipients.ToString(CultureInfo.InvariantCulture),
            ["_param_price_per_recipient"] = unitPrice
        };

        if (options.HasSysCode) fields["sys"] = options.SysCode;
        if (options.IsTest) fields["test"] = "1";
        if (options.HasPaymentPageSignatureKey) fields["signature"] = BuildStartSignature(fields, options.PaymentPageSignatureKey);
        return fields;
    }

    public static string BuildStartSignature(IReadOnlyDictionary<string, string> fields, string signatureKey) => BuildHmac(BuildNestedPayload(fields), signatureKey);

    public static string BuildCheck(IReadOnlyDictionary<string, string> fields, string checkValue) => BuildHmac(BuildNestedPayload(fields), checkValue);

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

    private static SortedDictionary<string, object> BuildNestedPayload(IReadOnlyDictionary<string, string> fields)
    {
        var payload = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var product = new SortedDictionary<string, object>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (CheckFieldNames.Contains(field.Key, StringComparer.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(field.Value)) continue;

            if (field.Key.StartsWith("products[0][", StringComparison.Ordinal) && field.Key.EndsWith("]", StringComparison.Ordinal))
            {
                var productKey = field.Key[12..^1];
                product[productKey] = field.Value;
                continue;
            }

            payload[field.Key] = field.Value;
        }

        if (product.Count > 0)
        {
            payload["products"] = new[] { product };
        }

        return payload;
    }

    private static string BuildHmac(object payload, string signatureKey)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions).Replace("/", "\\/");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signatureKey));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }
}

using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed class AliasInboundReplyMatchingService(
    IInboundReplyTokenService tokens,
    IMailingRepository mailings,
    IEmailNormalizer normalizer,
    IClientReplyAliasRepository aliases,
    IOutboundReplyMessageRepository outboundReplyMessages) : IInboundReplyMatchingService
{
    public InboundReplyMatchResult Match(EmailProviderInboundEvent inbound)
    {
        if (TryMatchLegacyToken(inbound) is { } legacy)
        {
            return legacy;
        }

        return MatchAlias(inbound);
    }

    private InboundReplyMatchResult? TryMatchLegacyToken(EmailProviderInboundEvent inbound)
    {
        var token = inbound.ReplyToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            token = ExtractLegacyTokenFromAddress(inbound.ToAddress);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var validation = tokens.Validate(token);
        if (!validation.Ok || validation.Payload is null)
        {
            return null;
        }

        var mailing = mailings.Get(validation.Payload.MailingId);
        if (mailing is null)
        {
            return InboundReplyMatchResult.Failure("mailing_not_found", "Рассылка не найдена.");
        }

        var owner = normalizer.Normalize(mailing.OwnerEmail);
        if (!string.Equals(owner, validation.Payload.ClientId, StringComparison.OrdinalIgnoreCase))
        {
            return InboundReplyMatchResult.Failure("client_mismatch", "Рассылка не принадлежит клиенту из reply token.");
        }

        var recipient = mailing.Recipients.FirstOrDefault(x =>
            x.Status == RecipientStatus.Accepted &&
            string.Equals(normalizer.Normalize(x.Email), validation.Payload.RecipientEmail, StringComparison.OrdinalIgnoreCase));
        if (recipient is null)
        {
            return InboundReplyMatchResult.Failure("recipient_not_found", "Получатель не найден в рассылке.");
        }

        var expectedKey = tokens.BuildRecipientKey(mailing.Id, validation.Payload.RecipientEmail);
        if (!string.Equals(expectedKey, validation.Payload.RecipientKey, StringComparison.OrdinalIgnoreCase))
        {
            return InboundReplyMatchResult.Failure("recipient_key_mismatch", "Ключ получателя не совпадает.");
        }

        return InboundReplyMatchResult.Success(mailing, validation.Payload.RecipientEmail);
    }

    private InboundReplyMatchResult MatchAlias(EmailProviderInboundEvent inbound)
    {
        var alias = ExtractAliasFromAddress(inbound.ToAddress);
        if (string.IsNullOrWhiteSpace(alias))
        {
            return InboundReplyMatchResult.Failure("reply_alias_missing", "Не удалось определить reply alias входящего адреса.");
        }

        var clientAlias = aliases.GetByAlias(alias);
        if (clientAlias is null)
        {
            return InboundReplyMatchResult.Failure("reply_alias_unknown", "Reply alias не найден.");
        }

        var messageIds = ExtractReferencedMessageIds(inbound.Headers);
        if (messageIds.Count == 0)
        {
            return InboundReplyMatchResult.Failure("reply_message_reference_missing", "В ответе нет In-Reply-To/References для сопоставления с исходящим письмом.");
        }

        var mapping = outboundReplyMessages.FindByMessageIds(messageIds).FirstOrDefault();
        if (mapping is null)
        {
            return InboundReplyMatchResult.Failure("reply_mapping_not_found", "Исходящее письмо не найдено по Message-ID из ответа.");
        }

        if (!string.Equals(mapping.ClientId, clientAlias.ClientId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mapping.ReplyAlias, clientAlias.Alias, StringComparison.OrdinalIgnoreCase))
        {
            return InboundReplyMatchResult.Failure("reply_alias_mapping_mismatch", "Reply alias не совпадает с сохранённым message mapping.");
        }

        var mailing = mailings.Get(mapping.MailingId);
        if (mailing is null)
        {
            return InboundReplyMatchResult.Failure("reply_mapping_mailing_not_found", "Рассылка из message mapping не найдена.");
        }

        var owner = normalizer.Normalize(mailing.OwnerEmail);
        if (!string.Equals(owner, mapping.ClientId, StringComparison.OrdinalIgnoreCase))
        {
            return InboundReplyMatchResult.Failure("reply_mapping_client_mismatch", "Message mapping не принадлежит владельцу рассылки.");
        }

        var recipient = mailing.Recipients.FirstOrDefault(x =>
            x.Status == RecipientStatus.Accepted &&
            string.Equals(normalizer.Normalize(x.Email), mapping.RecipientEmailNormalized, StringComparison.OrdinalIgnoreCase));
        if (recipient is null)
        {
            return InboundReplyMatchResult.Failure("reply_mapping_recipient_not_found", "Получатель из message mapping не найден в рассылке.");
        }

        return InboundReplyMatchResult.Success(mailing, mapping.RecipientEmailNormalized);
    }

    private static IReadOnlyCollection<string> ExtractReferencedMessageIds(IReadOnlyDictionary<string, string> headers)
    {
        var result = new List<string>();
        AddHeaderMessageIds(headers, "In-Reply-To", result);
        AddHeaderMessageIds(headers, "References", result);
        return result
            .Select(OutboundReplyMessageMapping.NormalizeMessageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddHeaderMessageIds(IReadOnlyDictionary<string, string> headers, string headerName, ICollection<string> result)
    {
        if (!headers.TryGetValue(headerName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var item in value.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = item.Trim().Trim(';', ',', '"', '\'');
            if (candidate.StartsWith('<') && candidate.EndsWith('>') && candidate.Contains(Convert.ToChar(64), StringComparison.Ordinal))
            {
                result.Add(candidate);
            }
        }
    }

    private static string? ExtractLegacyTokenFromAddress(string? toAddress)
    {
        var value = CleanAddress(toAddress);
        var at = value.IndexOf(Convert.ToChar(64));
        if (at <= 0)
        {
            return null;
        }

        var localPart = value[..at];
        if (localPart.StartsWith("reply+", StringComparison.OrdinalIgnoreCase) && localPart.Length > "reply+".Length)
        {
            return localPart["reply+".Length..];
        }

        return localPart.StartsWith("v1.", StringComparison.OrdinalIgnoreCase) ? localPart : null;
    }

    private static string? ExtractAliasFromAddress(string? toAddress)
    {
        var value = CleanAddress(toAddress);
        var at = value.IndexOf(Convert.ToChar(64));
        if (at <= 0)
        {
            return null;
        }

        var localPart = value[..at].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(localPart) ||
            localPart.StartsWith("reply+", StringComparison.OrdinalIgnoreCase) ||
            localPart.StartsWith("v1.", StringComparison.OrdinalIgnoreCase) ||
            localPart.Contains('+', StringComparison.Ordinal))
        {
            return null;
        }

        return localPart;
    }

    private static string CleanAddress(string? toAddress)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
        {
            return string.Empty;
        }

        var value = toAddress.Trim();
        var left = value.LastIndexOf('<');
        var right = value.LastIndexOf('>');
        if (left >= 0 && right > left)
        {
            value = value[(left + 1)..right];
        }

        return value.Trim().Trim('<', '>', ' ', '\t', '\r', '\n').ToLowerInvariant();
    }
}

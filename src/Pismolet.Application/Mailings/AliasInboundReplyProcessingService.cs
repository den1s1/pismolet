using System.Security.Cryptography;
using System.Text;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed class AliasInboundReplyProcessingService(
    IReplyEventRepository replies,
    IInboundReplyMatchingService matcher,
    IInboundReplyTokenService tokens,
    IClientReplyAliasRepository aliases,
    IBackgroundReplyQueue queue,
    IEmailProviderAdapter provider,
    IAuditLogger audit,
    InboundReplyOptions options) : IInboundReplyProcessingService
{
    public Task<InboundReplyProcessResult> ProcessAsync(EmailProviderInboundEvent inbound, RequestMetadata request, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var existing = replies.GetByProviderEventId(inbound.Provider, inbound.ProviderInboundEventId);
        if (existing is not null)
        {
            Audit("inbound_reply_duplicate", $"replyEventId={existing.Id};provider={inbound.Provider}", request);
            return Task.FromResult(new InboundReplyProcessResult("duplicate", correlationId, existing.Id));
        }

        var body = Truncate(inbound.TextBody ?? inbound.HtmlBody ?? string.Empty, Math.Max(0, options.MaxStoredBodyChars));
        var receivedAt = inbound.ReceivedAt == default ? DateTimeOffset.UtcNow : inbound.ReceivedAt.ToUniversalTime();
        var reply = ReplyEvent.Received(
            inbound.Provider,
            inbound.ProviderInboundEventId,
            inbound.FromEmail,
            inbound.ToAddress,
            tokens.HashToken(inbound.ReplyToken),
            SafeSubject(inbound.Subject),
            body,
            receivedAt,
            receivedAt.AddDays(Math.Clamp(options.BodyRetentionDays, 1, 60)),
            Hash(inbound.RawPayload));

        if (InboundReplyAutoReplyDetector.ShouldIgnore(inbound))
        {
            reply = replies.AddIfNotExists(reply.MarkAutoReply("Обнаружены признаки auto-reply/mail loop."));
            Audit("inbound_reply_auto_ignored", $"replyEventId={reply.Id};provider={inbound.Provider}", request);
            return Task.FromResult(new InboundReplyProcessResult("ignored_auto_reply", correlationId, reply.Id));
        }

        var match = matcher.Match(inbound);
        if (!match.Matched || match.Mailing is null || string.IsNullOrWhiteSpace(match.RecipientEmail))
        {
            var fallback = TryBuildKnownAliasFallback(inbound, match);
            if (fallback is not null)
            {
                reply = reply
                    .MarkKnownAliasFallback(fallback.ClientId, fallback.ClientId, match.ErrorCode, match.ErrorMessage)
                    .MarkQueuedForForward();
                reply = replies.AddIfNotExists(reply);
                queue.EnqueueForward(reply.Id);
                Audit("inbound_reply_known_alias_fallback_queued", $"replyEventId={reply.Id};clientHash={Hash(fallback.ClientId)};error={match.ErrorCode}", request);
                return Task.FromResult(new InboundReplyProcessResult("queued_for_forward", correlationId, reply.Id));
            }

            reply = replies.AddIfNotExists(reply.MarkUnmatched(match.ErrorCode, match.ErrorMessage));
            Audit("inbound_reply_unmatched", $"replyEventId={reply.Id};provider={inbound.Provider};error={match.ErrorCode}", request);
            return Task.FromResult(new InboundReplyProcessResult("unmatched", correlationId, reply.Id));
        }

        reply = reply
            .MarkMatched(match.Mailing.Id, match.Mailing.OwnerEmail, match.RecipientEmail, match.Mailing.OwnerEmail)
            .MarkQueuedForForward();
        reply = replies.AddIfNotExists(reply);
        queue.EnqueueForward(reply.Id);
        Audit("inbound_reply_queued", $"replyEventId={reply.Id};mailingId={match.Mailing.Id};emailHash={Hash(match.RecipientEmail)}", request);
        return Task.FromResult(new InboundReplyProcessResult("queued_for_forward", correlationId, reply.Id));
    }

    public async Task ExecuteForwardAsync(Guid replyEventId, CancellationToken cancellationToken)
    {
        var reply = replies.TryClaimForward(replyEventId);
        if (reply is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reply.ForwardToEmailNormalized))
        {
            replies.MarkForwardFailed(replyEventId, "missing_forward_to", "Не указан адрес пересылки клиента.");
            return;
        }

        EmailProviderSendResult result;
        try
        {
            result = await provider.ForwardReplyToClientAsync(reply, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            replies.MarkForwardQueued(replyEventId);
            return;
        }
        catch (Exception ex)
        {
            replies.MarkForwardFailed(replyEventId, "forward_exception", ex.Message);
            return;
        }

        if (result.Accepted)
        {
            replies.MarkForwarded(replyEventId);
        }
        else
        {
            replies.MarkForwardFailed(replyEventId, result.ErrorCode ?? "forward_failed", result.ErrorMessage ?? "Не удалось переслать ответ клиенту.");
        }
    }

    public Task<int> CleanupExpiredBodiesAsync(CancellationToken cancellationToken)
    {
        var expired = replies.FindExpiredBodies(DateTimeOffset.UtcNow, Math.Max(1, options.ForwardBatchSize));
        var count = 0;
        foreach (var item in expired)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            replies.MarkBodyDeleted(item.Id);
            count++;
        }

        return Task.FromResult(count);
    }

    private ClientReplyAlias? TryBuildKnownAliasFallback(EmailProviderInboundEvent inbound, InboundReplyMatchResult match)
    {
        if (!IsKnownAliasFallbackError(match.ErrorCode))
        {
            return null;
        }

        var alias = ExtractAlias(inbound.ToAddress);
        return string.IsNullOrWhiteSpace(alias) ? null : aliases.GetByAlias(alias);
    }

    private static bool IsKnownAliasFallbackError(string errorCode) => errorCode is
        "reply_message_reference_missing" or
        "reply_mapping_not_found" or
        "reply_mapping_mailing_not_found" or
        "reply_mapping_recipient_not_found";

    private static string? ExtractAlias(string? toAddress)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
        {
            return null;
        }

        var value = toAddress.Trim();
        var left = value.LastIndexOf('<');
        var right = value.LastIndexOf('>');
        if (left >= 0 && right > left)
        {
            value = value[(left + 1)..right];
        }

        value = value.Trim().Trim('<', '>', ' ', '\t', '\r', '\n').ToLowerInvariant();
        var at = value.IndexOf(Convert.ToChar(64));
        if (at <= 0)
        {
            return null;
        }

        var localPart = value[..at];
        return localPart.Contains('+') || localPart.StartsWith("reply+", StringComparison.OrdinalIgnoreCase) || localPart.StartsWith("v1.", StringComparison.OrdinalIgnoreCase)
            ? null
            : localPart;
    }

    private static string SafeSubject(string? subject)
    {
        var value = string.IsNullOrWhiteSpace(subject) ? "Без темы" : subject.Trim();
        return value.Length <= 160 ? value : value[..160];
    }

    private static string Truncate(string value, int max)
    {
        if (max <= 0 || string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = value.Trim();
        return clean.Length <= max ? clean : clean[..max] + "\n\n[Ответ обрезан по политике хранения MVP]";
    }

    private void Audit(string eventType, string context, RequestMetadata request) => audit.Write(new AuditRecord(DateTimeOffset.UtcNow, "inbound-reply", eventType, request.Ip, request.UserAgent, context));

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

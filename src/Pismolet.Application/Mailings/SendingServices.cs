using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Mailings;

public sealed record EmailRecipient(string Email);

public sealed record EmailMessage(
    Guid MailingId,
    EmailRecipient Recipient,
    string SenderName,
    string Subject,
    string PlainTextBody,
    string UnsubscribeUrl,
    string ServiceIdentifier,
    string ReplyToAddress,
    string ReplyToken,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record EmailProviderSendResult(bool Accepted, string? ProviderMessageId, string? ErrorCode, string? ErrorMessage)
{
    public static EmailProviderSendResult Success(string providerMessageId) => new(true, providerMessageId, null, null);

    public static EmailProviderSendResult Failure(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed record EmailProviderWebhookEvent(
    string Provider,
    string ProviderEventId,
    string? ProviderMessageId,
    Guid? MailingId,
    string? RecipientEmail,
    ProviderWebhookEventType EventType,
    DateTimeOffset OccurredAt,
    string? ReasonCode,
    string? ReasonMessage,
    string RawPayload);

public sealed record EmailProviderWebhookParseResult(bool Ok, string Error, EmailProviderWebhookEvent? Event)
{
    public static EmailProviderWebhookParseResult Success(EmailProviderWebhookEvent item) => new(true, string.Empty, item);

    public static EmailProviderWebhookParseResult Failure(string error) => new(false, error, null);
}

public sealed record EmailProviderInboundEvent(
    string Provider,
    string ProviderInboundEventId,
    string FromEmail,
    string ToAddress,
    string? ReplyToken,
    string Subject,
    string? TextBody,
    string? HtmlBody,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ReceivedAt,
    string RawPayload);

public sealed record EmailProviderInboundParseResult(bool Ok, string Error, EmailProviderInboundEvent? Event)
{
    public static EmailProviderInboundParseResult Success(EmailProviderInboundEvent item) => new(true, string.Empty, item);

    public static EmailProviderInboundParseResult Failure(string error) => new(false, error, null);
}

public sealed record MailingSendState(Mailing Mailing, MailingSendSummary Summary, IReadOnlyCollection<SendEvent> Events);

public sealed record MailingSendResult(bool Ok, string Error, MailingSendState? State)
{
    public static MailingSendResult Success(MailingSendState state) => new(true, string.Empty, state);

    public static MailingSendResult Failure(string error, MailingSendState? state = null) => new(false, error, state);
}

public sealed record ClientLimitUpdateResult(bool Ok, string Error, UserAccount? User)
{
    public static ClientLimitUpdateResult Success(UserAccount user) => new(true, string.Empty, user);

    public static ClientLimitUpdateResult Failure(string error) => new(false, error, null);
}

public sealed record MailingSendOptions(int BatchSize)
{
    public static MailingSendOptions Default { get; } = new(100);
}

public interface IEmailProviderAdapter
{
    string ProviderName { get; }

    Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);

    Task<EmailProviderWebhookParseResult> ParseWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);

    Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);

    Task<EmailProviderSendResult> ForwardReplyToClientAsync(ReplyEvent replyEvent, CancellationToken cancellationToken);
}

public interface IBackgroundMailingSendQueue
{
    void Enqueue(Guid mailingId);
}

public interface IMailingSendService
{
    MailingSendResult GetState(string userEmail, Guid mailingId);

    MailingSendResult StartSending(string userEmail, Guid mailingId, RequestMetadata request);

    MailingSendResult ResumeSending(string userEmail, Guid mailingId, RequestMetadata request);

    Task ExecuteQueuedBatchAsync(Guid mailingId, CancellationToken cancellationToken);
}

public interface IClientSendLimitAdminService
{
    ClientLimitUpdateResult UpdateDailyLimit(string clientEmail, int newDailyLimit, string adminEmail, RequestMetadata request);
}

public sealed class FakeEmailProviderAdapter(IFakeMailer fakeMailer) : IEmailProviderAdapter
{
    public string ProviderName => SendEvent.FakeProvider;

    public Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var email = message.Recipient.Email.Trim().ToLowerInvariant();
        if (email.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(EmailProviderSendResult.Failure("fake_failed", "Fake provider вернул тестовую ошибку для адреса."));
        }

        if (email.Contains("temp", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(EmailProviderSendResult.Failure("fake_temporary", "Fake provider вернул временную тестовую ошибку."));
        }

        var providerMessageId = BuildProviderMessageId(message.MailingId, email);
        fakeMailer.AddMailingMessage(email, message.Subject, message.UnsubscribeUrl, message.ReplyToAddress, message.ReplyToken, providerMessageId, message.PlainTextBody);
        return Task.FromResult(EmailProviderSendResult.Success(providerMessageId));
    }

    public Task<EmailProviderWebhookParseResult> ParseWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var providerEventId = ReadString(root, "providerEventId");
            var providerMessageId = ReadString(root, "providerMessageId");
            var eventTypeRaw = ReadString(root, "eventType");
            if (string.IsNullOrWhiteSpace(providerEventId) || string.IsNullOrWhiteSpace(eventTypeRaw))
            {
                return Task.FromResult(EmailProviderWebhookParseResult.Failure("Некорректный fake webhook payload."));
            }

            Guid? mailingId = null;
            if (Guid.TryParse(ReadString(root, "mailingId"), out var parsedMailingId))
            {
                mailingId = parsedMailingId;
            }

            var occurredAt = DateTimeOffset.UtcNow;
            if (DateTimeOffset.TryParse(ReadString(root, "occurredAt"), out var parsedOccurredAt))
            {
                occurredAt = parsedOccurredAt.ToUniversalTime();
            }

            var item = new EmailProviderWebhookEvent(
                ProviderName,
                providerEventId.Trim(),
                providerMessageId,
                mailingId,
                ReadString(root, "recipientEmail"),
                MapEventType(eventTypeRaw),
                occurredAt,
                ReadString(root, "reasonCode"),
                ReadString(root, "reasonMessage"),
                rawBody);

            return Task.FromResult(EmailProviderWebhookParseResult.Success(item));
        }
        catch (JsonException)
        {
            return Task.FromResult(EmailProviderWebhookParseResult.Failure("Некорректный JSON webhook payload."));
        }
    }

    public Task<EmailProviderInboundParseResult> ParseInboundWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var providerInboundEventId = ReadString(root, "providerInboundEventId");
            var from = ReadString(root, "from");
            var to = ReadString(root, "to");
            if (string.IsNullOrWhiteSpace(providerInboundEventId) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                return Task.FromResult(EmailProviderInboundParseResult.Failure("Некорректный fake inbound payload."));
            }

            var receivedAt = DateTimeOffset.UtcNow;
            if (DateTimeOffset.TryParse(ReadString(root, "receivedAt"), out var parsedReceivedAt))
            {
                receivedAt = parsedReceivedAt.ToUniversalTime();
            }

            var item = new EmailProviderInboundEvent(
                ProviderName,
                providerInboundEventId.Trim(),
                from.Trim().ToLowerInvariant(),
                to.Trim().ToLowerInvariant(),
                ReadString(root, "replyToken"),
                ReadString(root, "subject") ?? "Ответ без темы",
                ReadString(root, "textBody"),
                ReadString(root, "htmlBody"),
                ReadHeaders(root, headers),
                receivedAt,
                rawBody);

            return Task.FromResult(EmailProviderInboundParseResult.Success(item));
        }
        catch (JsonException)
        {
            return Task.FromResult(EmailProviderInboundParseResult.Failure("Некорректный JSON inbound payload."));
        }
    }

    public Task<EmailProviderSendResult> ForwardReplyToClientAsync(ReplyEvent replyEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replyEvent.ForwardToEmailNormalized))
        {
            return Task.FromResult(EmailProviderSendResult.Failure("missing_forward_to", "Не указан адрес пересылки клиента."));
        }

        var subject = $"Ответ на рассылку: {replyEvent.SubjectPreview}";
        var body = string.Join("\n\n",
            "Это пересланный ответ получателя через сервис Письмолёт.",
            $"От: {replyEvent.FromEmailNormalized}",
            $"Получен: {replyEvent.ReceivedAt:yyyy-MM-dd HH:mm} UTC",
            "Текст ответа:",
            string.IsNullOrWhiteSpace(replyEvent.BodyTextStored) ? "[Тело ответа уже удалено или не сохранялось]" : replyEvent.BodyTextStored);
        var providerMessageId = $"fake-forward-{replyEvent.Id:N}";
        fakeMailer.AddForwardedReply(replyEvent.ForwardToEmailNormalized, subject, replyEvent.FromEmailNormalized, body, providerMessageId);
        return Task.FromResult(EmailProviderSendResult.Success(providerMessageId));
    }

    public static string BuildProviderMessageId(Guid mailingId, string recipientEmail)
    {
        var raw = $"{mailingId:N}:{recipientEmail.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"fake-{mailingId:N}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    public static string BuildProviderEventId(string providerMessageId, ProviderWebhookEventType eventType) =>
        $"fake-event-{Hash($"{providerMessageId}:{eventType}")[..20]}";

    public static string BuildProviderInboundEventId(string providerMessageId, string fromEmail) =>
        $"fake-inbound-{Hash($"{providerMessageId}:{fromEmail}")[..20]}";

    private static ProviderWebhookEventType MapEventType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "accepted" => ProviderWebhookEventType.Accepted,
        "delivered" => ProviderWebhookEventType.Delivered,
        "soft_bounce" or "soft-bounce" or "temporary_failure" => ProviderWebhookEventType.SoftBounce,
        "hard_bounce" or "hard-bounce" or "permanent_failure" => ProviderWebhookEventType.HardBounce,
        "complaint" => ProviderWebhookEventType.Complaint,
        "rejected" => ProviderWebhookEventType.Rejected,
        _ => ProviderWebhookEventType.Unknown
    };

    private static IReadOnlyDictionary<string, string> ReadHeaders(JsonElement root, IReadOnlyDictionary<string, string> requestHeaders)
    {
        var result = new Dictionary<string, string>(requestHeaders, StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("headers", out var headersElement) && headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in headersElement.EnumerateObject())
            {
                result[item.Name] = item.Value.ToString();
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class MailingSendService(
    IMailingRepository mailings,
    IPaymentRepository payments,
    ISendEventRepository sendEvents,
    IGlobalSuppressionRepository suppressions,
    IClientSuppressionRepository clientSuppressions,
    IUserRepository users,
    IEmailProviderAdapter provider,
    IEmailNormalizer emailNormalizer,
    IUnsubscribeTokenService tokens,
    IInboundReplyTokenService replyTokens,
    IBackgroundMailingSendQueue queue,
    IAuditLogger auditLogger,
    MailingSendOptions options,
    IMailWarmupSendGate warmupGate) : IMailingSendService
{
    private const int MaxDailyLimit = 100000;

    public MailingSendResult GetState(string userEmail, Guid mailingId)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null)
        {
            return MailingSendResult.Failure("Рассылка не найдена.");
        }

        return BuildState(mailing);
    }

    public MailingSendResult StartSending(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null)
        {
            return MailingSendResult.Failure("Рассылка не найдена.");
        }

        if (mailing.Status is MailingStatus.Sending or MailingStatus.Sent or MailingStatus.Failed or MailingStatus.Paused)
        {
            return BuildState(mailing);
        }

        var validation = ValidateReadyToSend(mailing);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            return MailingSendResult.Failure(validation, BuildState(mailing).State);
        }

        var owner = users.GetByEmail(mailing.OwnerEmail);
        var dailyLimit = Math.Clamp(owner?.Profile.DailySendLimit ?? 0, 0, MaxDailyLimit);
        var usedToday = sendEvents.CountAcceptedForOwnerOnUtcDate(mailing.OwnerEmail, DateOnly.FromDateTime(DateTime.UtcNow));
        var available = Math.Max(0, dailyLimit - usedToday);
        var acceptedRecipients = mailing.Recipients
            .Where(x => x.Status == RecipientStatus.Accepted)
            .Select(x => emailNormalizer.Normalize(x.Email))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suppressedSet = suppressions.GetSuppressedSet(acceptedRecipients);
        var clientSuppressedSet = clientSuppressions.GetSuppressedSet(mailing.OwnerEmail, acceptedRecipients);

        foreach (var recipientEmail in acceptedRecipients)
        {
            var existing = sendEvents.Get(mailing.Id, recipientEmail);
            if (existing is not null)
            {
                continue;
            }

            var sendEvent = SendEvent.Pending(mailing.Id, mailing.OwnerEmail, recipientEmail);
            if (suppressedSet.Contains(recipientEmail))
            {
                sendEvents.Save(sendEvent.MarkSkipped(SendSkipReason.GlobalSuppression));
                AuditSuppressedSend(mailing, sendEvent, "suppressed_email_skipped_before_send", request.Ip, request.UserAgent);
                continue;
            }

            if (clientSuppressedSet.Contains(recipientEmail))
            {
                sendEvents.Save(sendEvent.MarkSkipped(SendSkipReason.ClientSuppression));
                AuditSuppressedSend(mailing, sendEvent, "client_suppressed_email_skipped_before_send", request.Ip, request.UserAgent);
                continue;
            }

            if (available <= 0)
            {
                sendEvents.Save(sendEvent.MarkPaused(SendSkipReason.DailyLimit));
                continue;
            }

            sendEvents.Save(sendEvent);
            available--;
        }

        var summary = sendEvents.GetSummary(mailing.Id, acceptedRecipients.Length);
        mailing = summary.Pending > 0
            ? mailing.WithStatus(MailingStatus.Sending)
            : summary.PausedByLimit > 0
                ? mailing.WithStatus(MailingStatus.Paused)
                : mailing.WithStatus(MailingStatus.Sent);
        mailings.Update(mailing);

        auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_requested", request.Ip, request.UserAgent, $"mailingId={mailing.Id};accepted={summary.AcceptedForSending};pending={summary.Pending};paused={summary.PausedByLimit};suppressed={summary.Suppressed};clientSuppressed={summary.ClientSuppressed}"));

        if (mailing.Status == MailingStatus.Sending)
        {
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_started", request.Ip, request.UserAgent, $"mailingId={mailing.Id};batchSize={options.BatchSize}"));
            queue.Enqueue(mailing.Id);
        }
        else if (mailing.Status == MailingStatus.Paused)
        {
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_paused_by_limit", request.Ip, request.UserAgent, $"mailingId={mailing.Id};paused={summary.PausedByLimit}"));
        }

        return BuildState(mailing);
    }

    public MailingSendResult ResumeSending(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null)
        {
            return MailingSendResult.Failure("Рассылка не найдена.");
        }

        if (!mailing.Status.CanResumeSending())
        {
            return MailingSendResult.Failure("Продолжить можно только приостановленную рассылку.", BuildState(mailing).State);
        }

        var owner = users.GetByEmail(mailing.OwnerEmail);
        var dailyLimit = Math.Clamp(owner?.Profile.DailySendLimit ?? 0, 0, MaxDailyLimit);
        var usedToday = sendEvents.CountAcceptedForOwnerOnUtcDate(mailing.OwnerEmail, DateOnly.FromDateTime(DateTime.UtcNow));
        var available = Math.Max(0, dailyLimit - usedToday);
        if (available <= 0)
        {
            return MailingSendResult.Failure("Дневной лимит всё ещё исчерпан.", BuildState(mailing).State);
        }

        var paused = sendEvents.ListByMailingId(mailing.Id)
            .Where(x => x.Status == SendEventStatus.Paused && x.Reason is SendSkipReason.DailyLimit or SendSkipReason.WarmupLimit)
            .OrderBy(x => x.CreatedAt)
            .Take(available)
            .ToArray();

        foreach (var item in paused)
        {
            sendEvents.Save(item.ResetForResume());
        }

        mailing = mailing.WithStatus(paused.Length > 0 ? MailingStatus.Sending : MailingStatus.Paused);
        mailings.Update(mailing);

        if (paused.Length > 0)
        {
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_started", request.Ip, request.UserAgent, $"mailingId={mailing.Id};resume=true;pending={paused.Length}"));
            queue.Enqueue(mailing.Id);
        }

        return BuildState(mailing);
    }

    public async Task ExecuteQueuedBatchAsync(Guid mailingId, CancellationToken cancellationToken)
    {
        var mailing = mailings.Get(mailingId);
        if (mailing is null || mailing.Status is MailingStatus.Sent or MailingStatus.Rejected)
        {
            return;
        }

        if (mailing.Status != MailingStatus.Sending)
        {
            return;
        }

        var draft = mailing.MessageDraft;
        if (draft is null)
        {
            mailings.Update(mailing.WithStatus(MailingStatus.Failed));
            return;
        }

        var pausedByWarmup = false;
        var batch = sendEvents.GetPendingBatch(mailing.Id, Math.Max(1, options.BatchSize));
        var suppressedSet = suppressions.GetSuppressedSet(batch.Select(x => x.RecipientEmail));
        var clientSuppressedSet = clientSuppressions.GetSuppressedSet(mailing.OwnerEmail, batch.Select(x => x.RecipientEmail));
        foreach (var sendEvent in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (suppressedSet.Contains(sendEvent.RecipientEmail))
            {
                sendEvents.Save(sendEvent.MarkSkipped(SendSkipReason.GlobalSuppression));
                AuditSuppressedSend(mailing, sendEvent, "suppressed_email_skipped_before_send", "background", "background");
                continue;
            }

            if (clientSuppressedSet.Contains(sendEvent.RecipientEmail))
            {
                sendEvents.Save(sendEvent.MarkSkipped(SendSkipReason.ClientSuppression));
                AuditSuppressedSend(mailing, sendEvent, "client_suppressed_email_skipped_before_send", "background", "background");
                continue;
            }

            var warmupDecision = warmupGate.Evaluate(mailing.OwnerEmail, sendEvent.RecipientEmail, DateTimeOffset.UtcNow);
            if (!warmupDecision.IsAllowed)
            {
                pausedByWarmup = true;
                sendEvents.Save(sendEvent.MarkPaused(SendSkipReason.WarmupLimit));
                auditLogger.Write(new AuditRecord(
                    DateTimeOffset.UtcNow,
                    mailing.OwnerEmail,
                    "mailing_send_paused_by_warmup",
                    "background",
                    "background",
                    $"mailingId={mailing.Id};eventId={sendEvent.Id};reason={warmupDecision.Reason};retryAfterSeconds={Math.Ceiling(warmupDecision.RetryAfter.TotalSeconds)}"));
                break;
            }

            var message = BuildEmailMessage(mailing, draft, sendEvent.RecipientEmail);
            var result = await provider.SendAsync(message, cancellationToken);
            if (result.Accepted && !string.IsNullOrWhiteSpace(result.ProviderMessageId))
            {
                sendEvents.Save(sendEvent.MarkAccepted(result.ProviderMessageId));
            }
            else
            {
                sendEvents.Save(sendEvent.MarkFailed(result.ErrorCode ?? "provider_failed", result.ErrorMessage ?? "Fake provider вернул ошибку."));
                auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "email_provider_send_failed", "background", "background", $"mailingId={mailing.Id};eventId={sendEvent.Id};errorCode={result.ErrorCode ?? "provider_failed"}"));
            }
        }

        var totalAccepted = mailing.Recipients.Count(x => x.Status == RecipientStatus.Accepted);
        var summary = sendEvents.GetSummary(mailing.Id, totalAccepted);
        if (pausedByWarmup)
        {
            mailings.Update(mailing.WithStatus(MailingStatus.Paused));
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_paused_by_limit", "background", "background", $"mailingId={mailing.Id};paused={summary.PausedByLimit};pending={summary.Pending};source=warmup"));
            return;
        }

        if (summary.Pending > 0)
        {
            mailings.Update(mailing.WithStatus(MailingStatus.Sending));
            queue.Enqueue(mailing.Id);
            return;
        }

        if (summary.PausedByLimit > 0)
        {
            mailings.Update(mailing.WithStatus(MailingStatus.Paused));
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_paused_by_limit", "background", "background", $"mailingId={mailing.Id};paused={summary.PausedByLimit}"));
            return;
        }

        if (summary.Failed > 0)
        {
            mailings.Update(mailing.WithStatus(MailingStatus.Failed));
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_failed", "background", "background", $"mailingId={mailing.Id};failed={summary.Failed};sent={summary.Sent}"));
            return;
        }

        mailings.Update(mailing.WithStatus(MailingStatus.Sent));
        auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_completed", "background", "background", $"mailingId={mailing.Id};sent={summary.Sent};suppressed={summary.Suppressed};clientSuppressed={summary.ClientSuppressed}"));
    }

    private MailingSendResult BuildState(Mailing mailing)
    {
        var totalAccepted = mailing.Recipients.Count(x => x.Status == RecipientStatus.Accepted);
        var events = sendEvents.ListByMailingId(mailing.Id);
        var summary = sendEvents.GetSummary(mailing.Id, totalAccepted);
        return MailingSendResult.Success(new MailingSendState(mailing, summary, events));
    }

    private string ValidateReadyToSend(Mailing mailing)
    {
        if (!mailing.Status.CanStartSending())
        {
            return "Отправку можно запустить только после оплаты и одобрения рассылки.";
        }

        var payment = payments.GetByMailingId(mailing.Id);
        if (payment?.Status != PaymentStatus.Paid)
        {
            return "Сначала оплатите рассылку.";
        }

        if (mailing.MessageDraft is null)
        {
            return "Сначала сохраните письмо.";
        }

        if (mailing.Recipients.All(x => x.Status != RecipientStatus.Accepted))
        {
            return "Нет принятых адресов для отправки.";
        }

        return string.Empty;
    }

    private EmailMessage BuildEmailMessage(Mailing mailing, MailingMessageDraft draft, string recipientEmail)
    {
        var token = tokens.Generate(mailing.Id, recipientEmail);
        var replyToken = replyTokens.Generate(mailing.Id, mailing.OwnerEmail, recipientEmail);
        var unsubscribeUrl = $"/unsubscribe/{token}";
        var replyToAddress = replyTokens.BuildReplyToAddress(replyToken);
        var source = mailing.Declaration?.BaseSource.ToRu() ?? "загруженной базы адресов";
        var reason = $"Почему вы получили это письмо: ваш адрес находится в базе «{source}», которую отправитель подтвердил перед рассылкой.";
        var serviceId = $"Служебный идентификатор рассылки: {mailing.PublicId}";
        var plain = string.Join("\n\n", draft.Body, reason, $"Отписаться от всех рассылок через сервис: {unsubscribeUrl}", "Отписка действует глобально для всех рассылок через Письмолёт.", serviceId);
        return new EmailMessage(
            mailing.Id,
            new EmailRecipient(recipientEmail),
            draft.SenderName,
            draft.Subject,
            plain,
            unsubscribeUrl,
            serviceId,
            replyToAddress,
            replyToken,
            new Dictionary<string, string>
            {
                ["mailingId"] = mailing.Id.ToString("N"),
                ["recipientKey"] = replyTokens.BuildRecipientKey(mailing.Id, recipientEmail),
                ["replyPurpose"] = "inbound_reply"
            });
    }

    private void AuditSuppressedSend(Mailing mailing, SendEvent sendEvent, string eventType, string ip, string userAgent) => auditLogger.Write(new AuditRecord(
        DateTimeOffset.UtcNow,
        mailing.OwnerEmail,
        eventType,
        ip,
        userAgent,
        $"mailingId={mailing.Id};eventId={sendEvent.Id};emailHash={Hash(sendEvent.RecipientEmail)}"));

    private Mailing? GetOwnedMailing(string userEmail, Guid mailingId)
    {
        var normalized = emailNormalizer.Normalize(userEmail);
        return string.IsNullOrWhiteSpace(normalized) ? null : mailings.GetForOwner(mailingId, normalized);
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class ClientSendLimitAdminService(IUserRepository users, IEmailNormalizer emailNormalizer, IAuditLogger auditLogger) : IClientSendLimitAdminService
{
    private const int MaxDailyLimit = 100000;

    public ClientLimitUpdateResult UpdateDailyLimit(string clientEmail, int newDailyLimit, string adminEmail, RequestMetadata request)
    {
        var normalizedClient = emailNormalizer.Normalize(clientEmail);
        var normalizedAdmin = emailNormalizer.Normalize(adminEmail);
        if (string.IsNullOrWhiteSpace(normalizedClient))
        {
            return ClientLimitUpdateResult.Failure("Укажите email клиента.");
        }

        if (newDailyLimit < 0 || newDailyLimit > MaxDailyLimit)
        {
            return ClientLimitUpdateResult.Failure($"Дневной лимит должен быть от 0 до {MaxDailyLimit}.");
        }

        var user = users.GetByEmail(normalizedClient);
        if (user is null)
        {
            return ClientLimitUpdateResult.Failure("Клиент не найден.");
        }

        var oldLimit = user.Profile.DailySendLimit;
        var updated = user with { Profile = user.Profile with { DailySendLimit = newDailyLimit } };
        users.Update(updated);
        auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, normalizedAdmin, "client_daily_send_limit_changed", request.Ip, request.UserAgent, $"clientEmail={normalizedClient};old={oldLimit};new={newDailyLimit}"));
        return ClientLimitUpdateResult.Success(updated);
    }
}
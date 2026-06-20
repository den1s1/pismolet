using System.Security.Cryptography;
using System.Text;
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
    string ServiceIdentifier);

public sealed record EmailProviderSendResult(bool Accepted, string? ProviderMessageId, string? ErrorCode, string? ErrorMessage)
{
    public static EmailProviderSendResult Success(string providerMessageId) => new(true, providerMessageId, null, null);

    public static EmailProviderSendResult Failure(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
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
    Task<EmailProviderSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
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

        fakeMailer.AddMailingMessage(email, message.Subject, message.UnsubscribeUrl);
        return Task.FromResult(EmailProviderSendResult.Success(BuildProviderMessageId(message.MailingId, email)));
    }

    private static string BuildProviderMessageId(Guid mailingId, string recipientEmail)
    {
        var raw = $"{mailingId:N}:{recipientEmail.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"fake-{mailingId:N}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }
}

public sealed class MailingSendService(
    IMailingRepository mailings,
    IPaymentRepository payments,
    ISendEventRepository sendEvents,
    IGlobalSuppressionRepository suppressions,
    IUserRepository users,
    IEmailProviderAdapter provider,
    IEmailNormalizer emailNormalizer,
    IUnsubscribeTokenService tokens,
    IBackgroundMailingSendQueue queue,
    IAuditLogger auditLogger,
    MailingSendOptions options) : IMailingSendService
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
                AuditSuppressedSend(mailing, sendEvent, request.Ip, request.UserAgent);
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

        auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_requested", request.Ip, request.UserAgent, $"mailingId={mailing.Id};accepted={summary.AcceptedForSending};pending={summary.Pending};paused={summary.PausedByLimit};suppressed={summary.Suppressed}"));

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
            .Where(x => x.Status == SendEventStatus.Paused && x.Reason == SendSkipReason.DailyLimit)
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

        var batch = sendEvents.GetPendingBatch(mailing.Id, Math.Max(1, options.BatchSize));
        var suppressedSet = suppressions.GetSuppressedSet(batch.Select(x => x.RecipientEmail));
        foreach (var sendEvent in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (suppressedSet.Contains(sendEvent.RecipientEmail))
            {
                sendEvents.Save(sendEvent.MarkSkipped(SendSkipReason.GlobalSuppression));
                AuditSuppressedSend(mailing, sendEvent, "background", "background");
                continue;
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
        auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_send_completed", "background", "background", $"mailingId={mailing.Id};sent={summary.Sent};suppressed={summary.Suppressed}"));
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
        var unsubscribeUrl = $"/unsubscribe/{token}";
        var source = mailing.Declaration?.BaseSource.ToRu() ?? "загруженной базы адресов";
        var reason = $"Почему вы получили это письмо: ваш адрес находится в базе «{source}», которую отправитель подтвердил перед рассылкой.";
        var serviceId = $"Служебный идентификатор рассылки: {mailing.PublicId}";
        var plain = string.Join("\n\n", draft.Body, reason, $"Отписаться от всех рассылок через сервис: {unsubscribeUrl}", "Отписка действует глобально для всех рассылок через Письмолёт.", serviceId);
        return new EmailMessage(mailing.Id, new EmailRecipient(recipientEmail), draft.SenderName, draft.Subject, plain, unsubscribeUrl, serviceId);
    }

    private void AuditSuppressedSend(Mailing mailing, SendEvent sendEvent, string ip, string userAgent) => auditLogger.Write(new AuditRecord(
        DateTimeOffset.UtcNow,
        mailing.OwnerEmail,
        "suppressed_email_skipped_before_send",
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

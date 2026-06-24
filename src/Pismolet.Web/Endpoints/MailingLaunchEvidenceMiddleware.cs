using System.Security.Claims;
using System.Text.Json;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public sealed class MailingLaunchEvidenceMiddleware(RequestDelegate next, ILogger<MailingLaunchEvidenceMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!IsLaunchPost(http, out var mailingId, out var action))
        {
            await next(http);
            return;
        }

        await next(http);

        if (http.Response.StatusCode is < 200 or >= 400)
        {
            return;
        }

        try
        {
            RecordLaunchEvidence(http, mailingId, action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось записать legal evidence запуска рассылки {MailingId}.", mailingId);
        }
    }

    private static bool IsLaunchPost(HttpContext http, out Guid mailingId, out string action)
    {
        mailingId = Guid.Empty;
        action = string.Empty;
        if (!HttpMethods.IsPost(http.Request.Method))
        {
            return false;
        }

        var segments = http.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();
        if (segments.Length != 4
            || !segments[0].Equals("mailings", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[1], out mailingId)
            || !segments[2].Equals("send", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        action = segments[3].ToLowerInvariant();
        return action is "start" or "resume";
    }

    private static void RecordLaunchEvidence(HttpContext http, Guid mailingId, string action)
    {
        var email = http.User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var mailings = http.RequestServices.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, email);
        if (mailing is null || !LooksLikeAcceptedLaunch(mailing, action))
        {
            return;
        }

        var legalEvidence = http.RequestServices.GetRequiredService<ILegalEvidenceService>();
        var importBatchId = mailing.Declaration?.ImportBatchId ?? mailing.LastImportBatch?.Id;
        var route = http.Request.Path.Value ?? $"/mailings/{mailingId}/send/{action}";
        var acceptedRecipients = mailing.Recipients.Count(x => x.Status == RecipientStatus.Accepted);
        var messageType = mailing.MessageDraft?.MessageType ?? MessageType.Transactional;
        var snapshot = $"{LegalEvidenceTextSnapshots.CampaignLaunchConfirmationText} Тема: {mailing.Subject}. Адресов к отправке: {acceptedRecipients}. Действие: {action}.";

        legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
            LegalEventTypes.CampaignLaunchConfirmedBeforePayment,
            email,
            email,
            importBatchId,
            mailingId,
            LegalDocumentKeys.CampaignLaunchConfirmation,
            BaseDeclarationText.CurrentVersion,
            legalEvidence.ComputeTextHash(LegalEvidenceTextSnapshots.CampaignLaunchConfirmationText),
            snapshot,
            LegalEventResults.Confirmed,
            http.Connection.RemoteIpAddress?.ToString(),
            http.Request.Headers.UserAgent.ToString(),
            route,
            JsonSerializer.Serialize(new
            {
                mailing_id = mailingId,
                import_batch_id = importBatchId,
                action,
                subject = mailing.Subject,
                status = mailing.Status.ToCode(),
                message_type = messageType.ToString(),
                accepted_recipients = acceptedRecipients
            })));
    }

    private static bool LooksLikeAcceptedLaunch(Mailing mailing, string action)
    {
        if (action == "start")
        {
            return mailing.Status is MailingStatus.Sending or MailingStatus.Sent or MailingStatus.Paused;
        }

        return mailing.Status is MailingStatus.Sending or MailingStatus.Sent or MailingStatus.Paused;
    }
}

public static class MailingLaunchEvidenceMiddlewareExtensions
{
    public static IApplicationBuilder UseMailingLaunchEvidenceCapture(this IApplicationBuilder app) =>
        app.UseMiddleware<MailingLaunchEvidenceMiddleware>();
}

using System.Security.Claims;
using System.Text.Json;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public sealed class LegalDeclarationEvidenceMiddleware(RequestDelegate next, ILogger<LegalDeclarationEvidenceMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!IsDeclarationPost(http, out var mailingId))
        {
            await next(http);
            return;
        }

        IFormCollection? form = null;
        try
        {
            form = await http.Request.ReadFormAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось прочитать форму декларации базы для legal evidence.");
        }

        await next(http);

        if (form is null || !IsSuccessfulDeclarationRedirect(http, mailingId))
        {
            return;
        }

        try
        {
            RecordDeclarationEvidence(http, mailingId, form);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось записать legal evidence для декларации базы {MailingId}.", mailingId);
        }
    }

    private static bool IsDeclarationPost(HttpContext http, out Guid mailingId)
    {
        mailingId = Guid.Empty;
        if (!HttpMethods.IsPost(http.Request.Method))
        {
            return false;
        }

        var segments = http.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();
        return segments.Length == 3
            && segments[0].Equals("mailings", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(segments[1], out mailingId)
            && segments[2].Equals("declaration", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulDeclarationRedirect(HttpContext http, Guid mailingId)
    {
        if (http.Response.StatusCode is < 300 or >= 400)
        {
            return false;
        }

        var location = http.Response.Headers.Location.ToString();
        return location.Equals($"/mailings/{mailingId}/message", StringComparison.OrdinalIgnoreCase);
    }

    private static void RecordDeclarationEvidence(HttpContext http, Guid mailingId, IFormCollection form)
    {
        var email = http.User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var legalEvidence = http.RequestServices.GetRequiredService<ILegalEvidenceService>();
        var mailings = http.RequestServices.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, email);
        var importBatchId = mailing?.Declaration?.ImportBatchId ?? mailing?.LastImportBatch?.Id;
        var metadata = ToMetadata(form, mailingId, importBatchId);
        var request = ToRequestMetadata(http);
        var baseSource = ParseBaseSource(form["baseSource"].ToString());
        var baseSourceLabel = baseSource is null ? form["baseSource"].ToString() : baseSource.Value.ToRu();
        var messageType = ParseMessageType(form["messageType"].ToString());
        var route = http.Request.Path.Value ?? $"/mailings/{mailingId}/declaration";

        legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
            LegalEventTypes.BaseSourceSelected,
            email,
            email,
            importBatchId,
            mailingId,
            null,
            null,
            null,
            $"Источник базы: {baseSourceLabel}",
            LegalEventResults.Confirmed,
            request.Ip,
            request.UserAgent,
            route,
            metadata));

        if (form.ContainsKey("baseLegality"))
        {
            legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
                LegalEventTypes.BaseLawfulnessDeclared,
                email,
                email,
                importBatchId,
                mailingId,
                LegalDocumentKeys.BaseLawfulnessDeclaration,
                BaseDeclarationText.CurrentVersion,
                legalEvidence.ComputeTextHash(BaseDeclarationText.Text),
                BaseDeclarationText.Text,
                LegalEventResults.Declared,
                request.Ip,
                request.UserAgent,
                route,
                metadata));

            legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
                LegalEventTypes.RecipientDataProcessingInstructionAccepted,
                email,
                email,
                importBatchId,
                mailingId,
                LegalDocumentKeys.RecipientDataProcessingInstruction,
                BaseDeclarationText.CurrentVersion,
                legalEvidence.ComputeTextHash(LegalEvidenceTextSnapshots.RecipientDataProcessingInstructionText),
                LegalEvidenceTextSnapshots.RecipientDataProcessingInstructionText,
                LegalEventResults.Accepted,
                request.Ip,
                request.UserAgent,
                route,
                metadata));
        }

        if (messageType == MessageType.Advertising && form.ContainsKey("advertisingConsent"))
        {
            legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
                LegalEventTypes.AdvertisingConsentDeclared,
                email,
                email,
                importBatchId,
                mailingId,
                LegalDocumentKeys.AdvertisingConsentDeclaration,
                BaseDeclarationText.CurrentVersion,
                legalEvidence.ComputeTextHash(LegalEvidenceTextSnapshots.AdvertisingConsentText),
                LegalEvidenceTextSnapshots.AdvertisingConsentText,
                LegalEventResults.Declared,
                request.Ip,
                request.UserAgent,
                route,
                metadata));
        }
    }

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string ToMetadata(IFormCollection form, Guid mailingId, Guid? importBatchId) => JsonSerializer.Serialize(new
    {
        mailing_id = mailingId,
        import_batch_id = importBatchId,
        base_source = form["baseSource"].ToString(),
        message_type = form["messageType"].ToString(),
        base_legality_checked = form.ContainsKey("baseLegality"),
        advertising_consent_checked = form.ContainsKey("advertisingConsent")
    });

    private static BaseSource? ParseBaseSource(string value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;

    private static MessageType ParseMessageType(string value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;
}

public static class LegalDeclarationEvidenceMiddlewareExtensions
{
    public static IApplicationBuilder UseLegalDeclarationEvidenceCapture(this IApplicationBuilder app) =>
        app.UseMiddleware<LegalDeclarationEvidenceMiddleware>();
}

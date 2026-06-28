using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class MailingConfirmationSubmitEndpoints
{
    public static IEndpointRouteBuilder MapMailingConfirmationSubmitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mailings/{id:guid}/confirmation", ConfirmAndContinueToPayment)
            .RequireAuthorization()
            .WithOrder(-3000);
        return app;
    }

    private static async Task<IResult> ConfirmAndContinueToPayment(Guid id, HttpContext http, IMailingService mailings, IMailingDeclarationService declarations, IMailingMessageService messages)
    {
        var email = http.User.FindFirstValue(ClaimTypes.Email);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync();
        var messageType = ParseMessageType(form["messageType"].ToString());
        var declaration = declarations.Confirm(new ConfirmMailingDeclarationCommand(
            email,
            id,
            ParseBaseSource(form["baseSource"].ToString()),
            form.ContainsKey("baseLegality"),
            form.ContainsKey("advertisingConsent"),
            messageType,
            ToRequestMetadata(http)));

        if (!declaration.Ok || declaration.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page(
                "Финальное подтверждение",
                $"<section class='panel'><h1>4. Финальное подтверждение</h1><p class='error-message'>{declaration.Error}</p><p><a href='/mailings/{id}/confirmation'>Вернуться к подтверждению</a></p></section>",
                authenticated: true));
        }

        var updated = declaration.Mailing;
        if (updated.MessageDraft is not null && updated.MessageDraft.MessageType != messageType)
        {
            var save = messages.Save(new SaveMailingMessageCommand(
                email,
                id,
                updated.MessageDraft.SenderName,
                updated.MessageDraft.Subject,
                updated.MessageDraft.Body,
                messageType,
                ToRequestMetadata(http),
                updated.MessageDraft.Attachments,
                updated.MessageDraft.BodyFormat));
            if (!save.Ok)
            {
                return HtmlRenderer.Html(HtmlRenderer.Page(
                    "Финальное подтверждение",
                    $"<section class='panel'><h1>4. Финальное подтверждение</h1><p class='error-message'>{save.Error}</p><p><a href='/mailings/{id}/confirmation'>Вернуться к подтверждению</a></p></section>",
                    authenticated: true));
            }
        }

        return Results.Redirect($"/mailings/{id}/payment");
    }

    private static BaseSource? ParseBaseSource(string? value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;

    private static MessageType ParseMessageType(string? value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;

    private static RequestMetadata ToRequestMetadata(HttpContext http) => new(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
}

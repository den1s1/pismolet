using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/payment", ShowPayment).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/payment/fake-start", StartPayment).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/payment/fake-success", ConfirmPayment).RequireAuthorization();
        return app;
    }

    private static IResult ShowPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = payments.GetPaymentReview(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Проверка и оплата", PaymentPage(result), authenticated: true));
    }

    private static IResult StartPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = payments.StartPayment(email, id, ToRequestMetadata(http));
        if (!result.Ok || result.Review?.Payment is null) return HtmlRenderer.Html(HtmlRenderer.Page("Проверка и оплата", PaymentPage(result), authenticated: true));
        var operationId = result.Review.Payment.Attempts.LastOrDefault()?.ProviderOperationId ?? string.Empty;
        return HtmlRenderer.Html(HtmlRenderer.Page("Тестовая оплата", ConfirmPage(id, operationId), authenticated: true));
    }

    private static IResult ConfirmPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var operationId = http.Request.Form["operationId"].ToString();
        var result = payments.ConfirmPayment(email, id, operationId, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Проверка и оплата", PaymentPage(result), authenticated: true));
    }

    private static string PaymentPage(MailingPaymentResult result)
    {
        if (!result.Ok || result.Review is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var review = result.Review;
        var payment = review.Payment;
        var paid = payment?.Status == Pismolet.Web.Domain.Mailings.PaymentStatus.Paid;
        var button = paid
            ? $"<p><span class='badge'>Оплачено</span></p><form method='post' action='/mailings/{review.Mailing.Id}/checks/start'><button class='button'>Проверить перед отправкой</button></form><p><a href='/mailings/{review.Mailing.Id}/checks'>Открыть статус проверки</a></p>"
            : $"<form method='post' action='/mailings/{review.Mailing.Id}/payment/fake-start'><button class='button'>Оплатить тестово</button></form>";

        return $"<section class='card'><h1>Проверка и оплата</h1><p class='muted'>{H(review.Mailing.Subject)}</p><p><span class='badge'>{H(review.Mailing.StatusRu)}</span></p><ul><li>Принято к оплате: {review.AcceptedRecipientsCount}</li><li>Исключено всего: {review.ExcludedRecipientsCount}</li><li>Дубли: {review.DuplicateRecipientsCount}</li><li>Невалидные: {review.InvalidRecipientsCount}</li><li>Глобально отписанные: {review.GloballySuppressedRecipientsCount}</li><li>Цена за адрес: {review.PricePerRecipient:0.##} {H(review.Currency)}</li><li>Итого: {review.TotalAmount:0.##} {H(review.Currency)}</li></ul><p class='muted'>Это тестовая оплата для MVP, реальные деньги не списываются.</p>{button}<p><a href='/mailings/{review.Mailing.Id}'>Вернуться к рассылке</a></p></section>";
    }

    private static string ConfirmPage(Guid mailingId, string operationId) => $"<section class='card'><h1>Тестовая оплата</h1><p>Fake-провайдер подготовил успешную оплату.</p><form method='post' action='/mailings/{mailingId}/payment/fake-success'><input type='hidden' name='operationId' value='{H(operationId)}'><button class='button'>Подтвердить успешную оплату</button></form></section>";

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

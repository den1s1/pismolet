using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
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

    private static async Task<IResult> StartPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");

        var review = payments.GetPaymentReview(email, id, ToRequestMetadata(http));
        if (!review.Ok || review.Review is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Проверка и оплата", PaymentPage(review), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync();
        var confirmationError = ValidatePaymentConfirmations(review.Review.Mailing, form);
        if (confirmationError is not null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Проверка и оплата", PaymentPage(review, confirmationError), authenticated: true));
        }

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

    private static string PaymentPage(MailingPaymentResult result, string? confirmationError = null)
    {
        if (!result.Ok || result.Review is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var review = result.Review;
        var mailing = review.Mailing;
        var stats = mailing.LastImportStats;
        var payment = review.Payment;
        var paid = payment?.Status == PaymentStatus.Paid;
        var excluded = Math.Max(0, stats.TotalRows - stats.Accepted);
        var issues = stats.Duplicates + stats.Invalid;
        var isPromo = mailing.MessageDraft?.MessageType == MessageType.Advertising;
        var alert = string.IsNullOrWhiteSpace(confirmationError) ? string.Empty : $"<p class='error-message'>{H(confirmationError)}</p>";
        var promoConfirm = isPromo
            ? "<label><input type='checkbox' name='advertisingConsent'> Подтверждаю согласие адресатов на промо-письмо</label>"
            : "<p class='muted'>Тип письма: информационное.</p>";
        var button = paid
            ? $"<p><span class='badge'>Оплачено</span></p><form method='post' action='/mailings/{mailing.Id}/checks/start'><button class='button'>Проверить перед отправкой</button></form><p><a href='/mailings/{mailing.Id}/checks'>Открыть статус проверки</a></p>"
            : $"<form method='post' action='/mailings/{mailing.Id}/payment/fake-start' class='confirmation-list'><h2>Подтверждения перед оплатой</h2><label><input type='checkbox' name='paymentBaseLegality'> Подтверждаю правомерность обработки адресов и отправки письма</label><label><input type='checkbox' name='paymentBaseOwnership'> Подтверждаю, что база не купленная и не чужая</label>{promoConfirm}<button class='button'>Оплатить тестово</button></form>";

        return $"<section class='wizard-shell payment-wizard'><div class='wizard-steps' aria-label='Шаги создания рассылки'><span class='wizard-step done'>Черновик</span><span class='wizard-step done'>1. Адреса</span><span class='wizard-step done'>2. Письмо</span><span class='wizard-step current'>3. Проверка и оплата</span></div><section class='panel'><div class='topline'><div><p class='eyebrow'>Шаг 3 из 3</p><h1>3. Проверка и оплата</h1><p class='muted'>{H(mailing.Subject)}</p></div><span class='badge warn'>{H(mailing.StatusRu)}</span></div>{alert}<div class='stats payment-stats'><div class='stat'><b>{stats.TotalRows}</b><span>Строк в файле <em title='Все строки последнего импорта'>i</em></span></div><div class='stat'><b>{stats.Accepted}</b><span>Принято к отправке <em title='Только эти адреса входят в расчёт'>i</em></span></div><div class='stat'><b>{issues}</b><span>Дубли и ошибки <em title='Дубликаты и ошибки не оплачиваются'>i</em></span></div><div class='stat'><b>{excluded}</b><span>Исключено всего <em title='Исключённые адреса не оплачиваются'>i</em></span></div><div class='stat'><b>{stats.GloballySuppressed}</b><span>Ранее отписались <em title='Такие адреса исключаются сервисом'>i</em></span></div></div><div class='payment-grid'><section class='box cost-card'><h2>Расчёт стоимости</h2><dl class='cost-list'><div><dt>Принято писем</dt><dd>{stats.Accepted}</dd></div><div><dt>Цена за письмо</dt><dd>{review.PricePerRecipient:0.##} {H(review.Currency)}</dd></div><div><dt>Исключённые адреса</dt><dd>{excluded} - не оплачиваются</dd></div><div class='total'><dt>Итого к оплате</dt><dd>{review.TotalAmount:0.##} {H(review.Currency)}</dd></div></dl><p class='muted'>Тестовая оплата для MVP.</p></section><section class='box confirmation-card'>{button}</section></div><div class='actions'><a class='btn secondary' href='/mailings/{mailing.Id}/message'>Назад к письму</a><a class='btn ghost' href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></div></section></section>";
    }

    private static string ConfirmPage(Guid mailingId, string operationId) => $"<section class='card'><h1>Тестовая оплата</h1><p>Fake-провайдер подготовил успешную оплату. Статус рассылки: ожидает оплаты.</p><form method='post' action='/mailings/{mailingId}/payment/fake-success'><input type='hidden' name='operationId' value='{H(operationId)}'><button class='button'>Подтвердить успешную оплату</button></form></section>";

    private static string? ValidatePaymentConfirmations(Mailing mailing, IFormCollection form)
    {
        if (!form.ContainsKey("paymentBaseLegality")) return "Подтвердите правомерность обработки адресов и отправки письма.";
        if (!form.ContainsKey("paymentBaseOwnership")) return "Подтвердите, что база не купленная и не чужая.";
        if (mailing.MessageDraft?.MessageType == MessageType.Advertising && !form.ContainsKey("advertisingConsent")) return "Для промо-письма подтвердите согласие адресатов.";
        return null;
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

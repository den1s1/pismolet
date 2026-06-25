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
        return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(result), authenticated: true));
    }

    private static async Task<IResult> StartPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");

        var review = payments.GetPaymentReview(email, id, ToRequestMetadata(http));
        if (!review.Ok || review.Review is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(review), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync();
        var confirmationError = ValidatePaymentConfirmations(review.Review.Mailing, form);
        if (confirmationError is not null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(review, confirmationError), authenticated: true));
        }

        var result = payments.StartPayment(email, id, ToRequestMetadata(http));
        if (!result.Ok || result.Review?.Payment is null) return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(result), authenticated: true));
        var operationId = result.Review.Payment.Attempts.LastOrDefault()?.ProviderOperationId ?? string.Empty;
        return HtmlRenderer.Html(HtmlRenderer.Page("Тестовая оплата", ConfirmPage(id, operationId), authenticated: true));
    }

    private static IResult ConfirmPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var operationId = http.Request.Form["operationId"].ToString();
        var result = payments.ConfirmPayment(email, id, operationId, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(result), authenticated: true));
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
        var cannotSend = Math.Max(0, stats.ClientSuppressed);
        var isPromo = mailing.MessageDraft?.MessageType == MessageType.Advertising;
        var alert = string.IsNullOrWhiteSpace(confirmationError) ? string.Empty : $"<p class='error-message'>{H(confirmationError)}</p>";
        var promoConfirm = isPromo
            ? $"<label class='check'><input type='checkbox' name='advertisingConsent'><span>Я <a href='/legal/advertising-consent?returnUrl=/mailings/{mailing.Id}/payment'>подтверждаю наличие согласия на рекламную рассылку</a>.</span></label>"
            : string.Empty;
        var payButtonText = $"Оплатить {review.TotalAmount:0.##} ₽ и запустить";
        var button = paid
            ? $"<p><span class='badge'>Оплачено</span></p><form method='post' action='/mailings/{mailing.Id}/checks/start'><button class='button'>Проверить перед отправкой</button></form><p><a href='/mailings/{mailing.Id}/checks'>Открыть статус проверки</a></p>"
            : $"<form method='post' action='/mailings/{mailing.Id}/payment/fake-start' class='confirmation-list checks'><h2>Подтверждения перед оплатой</h2><label class='check'><input type='checkbox' name='paymentBaseLegality'><span>Я подтверждаю, что имею законное основание для обработки загруженных адресов и отправки писем этим адресатам.</span></label><label class='check'><input type='checkbox' name='paymentBaseOwnership'><span>Я не использую купленную или чужую базу.</span></label>{promoConfirm}<div class='notice warn'>Оплата не гарантирует отправку запрещённых или рискованных рассылок. При риске рассылка уйдёт на проверку.</div><button class='button full-pay-button'>{H(payButtonText)}</button></form>";

        return $@"
<section class='wizard-shell payment-wizard'>
  <div class='wizard-steps' aria-label='Шаги создания рассылки'>
    <span class='wizard-step done'>1. Адреса</span>
    <span class='wizard-step done'>2. Письмо</span>
    <span class='wizard-step current'>3. Расчёт и оплата</span>
    <span class='wizard-step'>4. Готово</span>
  </div>
  <section class='panel'>
    <div class='topline'>
      <div>
        <p class='eyebrow'>Шаг 3 из 4</p>
        <h1>3. Проверьте расчёт и оплатите</h1>
        <p class='muted'>Оплата будет только за письма, принятые к отправке.</p>
      </div>
      <span class='badge warn'>{H(mailing.StatusRu)}</span>
    </div>
    {alert}
    <div class='stats payment-stats'>
      <div class='stat'><b>{stats.TotalRows}</b><span>строки в файле</span></div>
      <div class='stat'><b>{stats.Accepted}</b><span>принято к отправке</span></div>
      <div class='stat'><b>{issues}</b><span>дубли и ошибки</span></div>
      <div class='stat'><b>{cannotSend}</b><span>не сможем отправить</span></div>
      <div class='stat'><b>{stats.GloballySuppressed}</b><span>отписались</span></div>
    </div>
    <div class='payment-grid'>
      <section class='box confirmation-card'>{button}</section>
      <section class='box cost-card pay-card'>
        <div class='pay-summary-line'><small>К оплате</small><strong class='sum'>{review.TotalAmount:0.##} ₽</strong></div>
        <p>{stats.Accepted} письмо × {review.PricePerRecipient:0.##} ₽. За исключённые {excluded} адрес не платите.</p>
        <dl class='cost-list'>
          <div><dt>Принято писем</dt><dd>{stats.Accepted}</dd></div>
          <div><dt>Цена за письмо</dt><dd>{review.PricePerRecipient:0.##} {H(review.Currency)}</dd></div>
          <div><dt>Не списываем за исключённые</dt><dd>{excluded}</dd></div>
        </dl>
      </section>
    </div>
    <div class='actions'><a class='btn secondary' href='/mailings/{mailing.Id}/message'>Назад к письму</a><a class='btn ghost' href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></div>
  </section>
</section>";
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

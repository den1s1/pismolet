using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class ProdamusPaymentEndpoints
{
    private static readonly string[] CallbackDiagnosticFieldNames =
    {
        "order_id", "order_num", "order", "payment_id", "paymentId", "id",
        "order_sum", "amount", "sum", "payment_amount", "paid_amount", "products[0][price]", "products[0][quantity]",
        "payment_status", "status", "order_status", "state", "currency", "sys", "date", "payment_date", "transaction_id", "payment_type"
    };

    public static IEndpointRouteBuilder MapProdamusPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/payment", ShowPayment).RequireAuthorization().WithOrder(-4000);
        app.MapPost("/mailings/{id:guid}/payment/start", StartPayment).RequireAuthorization().WithOrder(-4000);
        app.MapMethods("/payments/prodamus/result", new[] { "GET", "POST" }, Result);
        app.MapMethods("/payments/prodamus/success", new[] { "GET", "POST" }, Success);
        app.MapMethods("/payments/prodamus/fail", new[] { "GET", "POST" }, Fail);
        return app;
    }

    private static IResult ShowPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = payments.GetPaymentReview(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Оплата", PaymentPage(result), authenticated: true));
    }

    private static async Task<IResult> StartPayment(Guid id, HttpContext http, IMailingPaymentService payments, ProdamusOptions prodamus, IMailingReviewService reviews)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var review = payments.GetPaymentReview(email, id, ToRequestMetadata(http));
        if (!review.Ok || review.Review is null) return HtmlRenderer.Html(HtmlRenderer.Page("Оплата", PaymentPage(review), authenticated: true));

        var form = await http.Request.ReadFormAsync();
        var confirmationError = ValidatePaymentConfirmations(review.Review.Mailing, form) ?? ValidatePaymentPage(prodamus);
        if (confirmationError is not null) return HtmlRenderer.Html(HtmlRenderer.Page("Оплата", PaymentPage(review, confirmationError), authenticated: true));

        var result = payments.StartPayment(email, id, ToRequestMetadata(http));
        if (!result.Ok || result.Review?.Payment is null) return HtmlRenderer.Html(HtmlRenderer.Page("Оплата", PaymentPage(result), authenticated: true));
        if (result.Review.Payment.Status == PaymentStatus.Paid)
        {
            reviews.StartChecks(result.Review.Mailing.OwnerEmail, id, ToRequestMetadata(http));
            return Results.Redirect($"/mailings/{id}/send");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Переход к оплате", AutoSubmitPage(result.Review, prodamus, http), authenticated: true));
    }

    private static async Task<IResult> Result(HttpContext http, IMailingPaymentService payments, IPaymentRepository paymentRepository, ProdamusOptions prodamus, IMailingReviewService reviews, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Pismolet.Payments.Prodamus");
        var fields = await ReadFields(http);
        if (!ValidateCallback(fields, prodamus, out var error)) return CallbackBadRequest(logger, error, fields);
        if (!ProdamusPaymentForm.TryGetOperationId(fields, out var operationId)) return CallbackBadRequest(logger, "Не передан идентификатор заказа Prodamus.", fields);
        var amountError = ValidatePaymentAmount(paymentRepository, operationId, fields);
        if (amountError is not null) return CallbackBadRequest(logger, amountError, fields);
        if (!ProdamusPaymentForm.IsPaidCallback(fields)) return CallbackBadRequest(logger, "Prodamus не подтвердил успешный статус платежа.", fields);

        var result = payments.ConfirmProviderPayment(operationId, ToRequestMetadata(http), CallbackSummary(fields));
        if (!result.Ok) return CallbackBadRequest(logger, result.Error, fields);
        var payment = paymentRepository.GetByProviderOperationId(operationId);
        if (payment is not null) reviews.StartChecks(payment.OwnerEmail, payment.MailingId, ToRequestMetadata(http));
        return Results.Text("OK", "text/plain; charset=utf-8");
    }

    private static async Task<IResult> Success(HttpContext http, IPaymentRepository paymentRepository, IMailingReviewService reviews)
    {
        var fields = await ReadFields(http);
        var authenticated = IsAuthenticated(http);
        if (!ProdamusPaymentForm.TryGetOperationId(fields, out var operationId))
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Успешная оплата", SuccessPage(null, string.Empty, "Переход после оплаты получен. Окончательный статус меняет только уведомление Prodamus.", authenticated), authenticated: authenticated));
        }

        var payment = paymentRepository.GetByProviderOperationId(operationId);
        if (payment?.Status == PaymentStatus.Paid)
        {
            reviews.StartChecks(payment.OwnerEmail, payment.MailingId, ToRequestMetadata(http));
            return Results.Redirect($"/mailings/{payment.MailingId}/send");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Успешная оплата", SuccessPage(payment, operationId, "Переход после оплаты получен. Ждём уведомление Prodamus.", authenticated), authenticated: authenticated));
    }

    private static async Task<IResult> Fail(HttpContext http, IPaymentRepository paymentRepository)
    {
        var fields = await ReadFields(http);
        var payment = ProdamusPaymentForm.TryGetOperationId(fields, out var operationId) ? paymentRepository.GetByProviderOperationId(operationId) : null;
        return HtmlRenderer.Html(HtmlRenderer.Page("Оплата не завершена", FailPage(payment, operationId), authenticated: IsAuthenticated(http)));
    }

    private static string PaymentPage(MailingPaymentResult result, string? confirmationError = null)
    {
        if (!result.Ok || result.Review is null) return HtmlRenderer.Error(result.Error);
        var review = result.Review;
        var mailing = review.Mailing;
        var stats = mailing.LastImportStats;
        var paid = review.Payment?.Status == PaymentStatus.Paid;
        var excluded = Math.Max(0, stats.TotalRows - stats.Accepted);
        var isPromo = mailing.MessageDraft?.MessageType == MessageType.Advertising;
        var hasAdvertisingConsent = mailing.Declaration?.IsAdvertisingConsentConfirmed == true;
        var alert = string.IsNullOrWhiteSpace(confirmationError) ? string.Empty : $"<p class='error-message'>{H(confirmationError)}</p>";
        var paymentRulesHref = $"/legal/payment-and-refund?returnUrl=/mailings/{mailing.Id}/payment";
        var button = paid
            ? $"<p><span class='badge'>Оплачено</span></p><form method='post' action='/mailings/{mailing.Id}/checks/start'><button class='button'>Перейти к запуску</button></form>"
            : PaymentAction(mailing, isPromo, hasAdvertisingConsent, paymentRulesHref, $"Оплатить {review.TotalAmount:0.##} ₽");

        return $@"
<section class='wizard-shell payment-wizard'>
  <!-- legacy-smoke: 3. Проверьте расчёт и оплатите -->
  <!-- legacy-ui: payment-legal-summary Подтверждения базы Источник базы Тип письма Правомерность базы Рекламное согласие -->
  <div class='wizard-steps' aria-label='Шаги создания рассылки'><span class='wizard-step done'>1. Письмо</span><span class='wizard-step done'>2. Адресаты</span><span class='wizard-step done'>3. Просмотр списка</span><span class='wizard-step done'>4. Подтверждение</span><span class='wizard-step current'>5. Оплата</span></div>
  <section class='panel'>
    <div class='topline'><div><p class='eyebrow'>Шаг 5 из 5</p><h1>5. Оплатите рассылку</h1></div><span class='badge warn'>{H(mailing.StatusRu)}</span></div>
    {alert}
    <div class='stats payment-stats payment-key-stats'><div class='stat'><b>{stats.Accepted}</b><span>принято к отправке</span></div><div class='stat'><b>{excluded}</b><span>исключено из расчёта</span></div><div class='stat'><b>{review.TotalAmount:0.##} ₽</b><span>к оплате</span></div></div>
    <div class='payment-grid'><section class='box cost-card pay-card'><div class='pay-summary-line'><small>К оплате</small><strong class='sum'>{review.TotalAmount:0.##} ₽</strong></div><p>{stats.Accepted} письмо × {review.PricePerRecipient:0.##} ₽. За исключённые {excluded} адрес не платите.</p><p class='muted'>Правила оплаты, запуска и возвратов: <a href='{paymentRulesHref}'>открыть документ</a>.</p></section><section class='box confirmation-card'>{button}</section></div>
    <div class='actions'><a class='btn secondary' href='/mailings/{mailing.Id}/confirmation'>Назад к подтверждению</a><a class='btn ghost' href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></div>
  </section>
</section>";
    }

    private static string PaymentAction(Mailing mailing, bool isPromo, bool hasAdvertisingConsent, string paymentRulesHref, string payButtonText)
    {
        if (isPromo && !hasAdvertisingConsent)
        {
            return $"<h2>Нужно подтвердить рекламное согласие</h2><p class='notice warn'>Это рекламная рассылка. Вернитесь на финальное подтверждение и подтвердите наличие рекламного согласия адресатов.</p><div class='actions'><a class='button' href='/mailings/{mailing.Id}/confirmation'>Вернуться к подтверждению</a><a class='btn secondary' href='/mailings/{mailing.Id}/recipients'>Вернуться к адресатам</a></div>";
        }

        return $"<form method='post' action='/mailings/{mailing.Id}/payment/start' class='confirmation-list checks' aria-label='Финальное подтверждение'><label class='check'><input type='checkbox' name='campaignLaunchConfirmation'><span>Я понимаю сумму к оплате и условия запуска после оплаты и проверок. <a href='{paymentRulesHref}'>Правила оплаты, запуска и возвратов</a>.</span></label><button class='button full-pay-button'>{H(payButtonText)}</button></form>";
    }

    private static string AutoSubmitPage(MailingPaymentReview review, ProdamusOptions prodamus, HttpContext http)
    {
        var payment = review.Payment ?? throw new InvalidOperationException("Payment is required after StartPayment.");
        var operationId = payment.Attempts.LastOrDefault(x => x.Provider == ProdamusPaymentForm.ProviderName)?.ProviderOperationId ?? ProdamusPaymentForm.BuildOrderId(payment.Id);
        var successUrl = AbsoluteUrl(http, $"/payments/prodamus/success?order_num={WebUtility.UrlEncode(operationId)}");
        var failUrl = AbsoluteUrl(http, $"/payments/prodamus/fail?order_num={WebUtility.UrlEncode(operationId)}");
        var resultUrl = AbsoluteUrl(http, "/payments/prodamus/result");
        var fields = ProdamusPaymentForm.BuildStartFields(review.Mailing.Id, review.Mailing.PublicId, payment.OwnerEmail, operationId, payment.AcceptedRecipientsCount, payment.ExcludedRecipientsCount, payment.PricePerRecipient, payment.TotalAmount, payment.Currency, prodamus, successUrl, failUrl, resultUrl);
        var payUrl = BuildPaymentUrl(prodamus.PaymentPageUrl, fields);
        return $"<section class='payment-autosubmit' aria-live='polite'><h1>Переходим на платёжную страницу</h1><p class='muted'>Сейчас откроется платёжная страница Prodamus. Если переход не произошёл автоматически, нажмите кнопку ниже.</p><p><a class='button full-pay-button' href='{H(payUrl)}'>Продолжить оплату</a></p><p><a class='btn secondary' href='/mailings/{review.Mailing.Id}/payment'>Вернуться к расчёту</a></p></section><script>window.location.replace({JsString(payUrl)});</script>";
    }

    private static string SuccessPage(Payment? payment, string operationId, string message, bool authenticated)
    {
        var fallback = authenticated ? "<p><a class='btn secondary' href='/dashboard'>В личный кабинет</a></p>" : "<p><a class='btn secondary' href='/'>На главную</a></p>";
        var next = payment is null ? fallback : payment.Status == PaymentStatus.Paid ? $"<p><a class='btn' href='/mailings/{payment.MailingId}/send'>Открыть запуск рассылки</a></p>" : $"<div class='notice warn'>Мы получили возврат из платёжного сервиса и ждём серверное подтверждение оплаты. Финальный статус меняет только result URL.</div><p><a class='btn' href='/mailings/{payment.MailingId}/send'>Проверить статус и продолжить</a></p><p><a href='/dashboard'>В личный кабинет</a></p>";
        var operation = string.IsNullOrWhiteSpace(operationId) ? string.Empty : $"<p class='muted'>Заказ: {H(operationId)}</p>";
        return $"<section class='panel'><h1>Переход после оплаты получен</h1><p>{H(message)}</p>{operation}{next}</section>";
    }

    private static string FailPage(Payment? payment, string operationId)
    {
        var retry = payment is null ? "<p><a class='btn secondary' href='/dashboard'>В личный кабинет</a></p>" : $"<p><a class='btn' href='/mailings/{payment.MailingId}/payment'>Повторить оплату</a></p>";
        var operation = string.IsNullOrWhiteSpace(operationId) ? string.Empty : $"<p class='muted'>Заказ: {H(operationId)}</p>";
        return $"<section class='panel'><h1>Оплата не завершена</h1><p>Платёжная страница вернула пользователя без успешного платежа. Статус рассылки не менялся.</p>{operation}{retry}</section>";
    }

    private static async Task<Dictionary<string, string>> ReadFields(HttpContext http)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in http.Request.Query) fields[item.Key] = item.Value.ToString();
        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync();
            foreach (var item in form) fields[item.Key] = item.Value.ToString();
        }
        return fields;
    }

    private static bool ValidateCallback(IReadOnlyDictionary<string, string> fields, ProdamusOptions prodamus, out string error)
    {
        if (!prodamus.CallbackCheckRequired)
        {
            error = string.Empty;
            return true;
        }

        var check = ProdamusPaymentForm.ExtractCheck(fields);
        if (string.IsNullOrWhiteSpace(check)) { error = "Не передана контрольная строка Prodamus."; return false; }
        if (!prodamus.HasCallbackCheckValue || !ProdamusPaymentForm.VerifyCheck(fields, check, prodamus.CallbackCheckValue)) { error = "Некорректная контрольная строка Prodamus."; return false; }
        error = string.Empty;
        return true;
    }

    private static string? ValidatePaymentAmount(IPaymentRepository paymentRepository, string operationId, IReadOnlyDictionary<string, string> fields)
    {
        var payment = paymentRepository.GetByProviderOperationId(operationId);
        if (payment is null) return "Платёжная попытка не найдена.";
        if (!ProdamusPaymentForm.TryGetAmount(fields, out var actual)) return "Некорректная сумма платежа.";
        return actual == payment.TotalAmount ? null : "Сумма платежа не совпадает с заказом.";
    }

    private static string? ValidatePaymentPage(ProdamusOptions prodamus)
    {
        if (!prodamus.HasPaymentPageUrl)
        {
            return "Платёжная страница Prodamus не настроена. Укажите URL в Prodamus:PaymentPageUrl.";
        }

        if (!Uri.TryCreate(prodamus.PaymentPageUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "Некорректный URL платёжной страницы Prodamus.";
        }

        if (!prodamus.HasPaymentPageSignatureKey)
        {
            return "Ключ HMAC SHA-256 для Prodamus не настроен. Укажите его в Prodamus:PaymentPageSignatureKey.";
        }

        return null;
    }

    private static IResult CallbackBadRequest(ILogger logger, string reason, IReadOnlyDictionary<string, string> fields)
    {
        logger.LogWarning("Prodamus callback rejected: {Reason}. Safe fields: {Fields}", reason, SafeCallbackDiagnostics(fields));
        return Results.BadRequest(reason);
    }

    private static string SafeCallbackDiagnostics(IReadOnlyDictionary<string, string> fields)
    {
        var parts = CallbackDiagnosticFieldNames
            .Where(fields.ContainsKey)
            .Select(key => $"{key}={TrimDiagnosticValue(fields[key])}");
        var keys = string.Join(',', fields.Keys.OrderBy(key => key, StringComparer.Ordinal));
        return string.Join(';', parts) + $"; field_count={fields.Count}; keys={keys}";
    }

    private static string TrimDiagnosticValue(string value) => value.Length <= 80 ? value : value[..80] + "...";

    private static string CallbackSummary(IReadOnlyDictionary<string, string> fields)
    {
        var parts = new[] { "order_id", "order_num", "order_sum", "amount", "sum", "payment_status", "status" }
            .Where(fields.ContainsKey)
            .Select(key => $"{key}={fields[key]}");
        return string.Join(';', parts);
    }

    private static string BuildPaymentUrl(string paymentPageUrl, IReadOnlyDictionary<string, string> fields)
    {
        var separator = paymentPageUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var query = string.Join("&", fields.Select(field => $"{WebUtility.UrlEncode(field.Key)}={WebUtility.UrlEncode(field.Value)}"));
        return paymentPageUrl + separator + query;
    }

    private static string JsString(string value) => "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal).Replace("</", "<\\/", StringComparison.Ordinal) + "'";
    private static string AbsoluteUrl(HttpContext http, string path) => $"{RequestScheme(http)}://{http.Request.Host}{path}";
    private static string RequestScheme(HttpContext http) => http.Request.Headers.TryGetValue("X-Forwarded-Proto", out var forwardedProto) && !string.IsNullOrWhiteSpace(forwardedProto.ToString()) ? forwardedProto.ToString().Split(',')[0].Trim() : http.Request.Scheme;
    private static string? ValidatePaymentConfirmations(Mailing mailing, IFormCollection form) => !form.ContainsKey("campaignLaunchConfirmation") ? "Подтвердите правила оплаты и запуска." : mailing.MessageDraft?.MessageType == MessageType.Advertising && mailing.Declaration?.IsAdvertisingConsentConfirmed != true ? "Для рекламной рассылки сначала подтвердите рекламное согласие адресатов на финальном подтверждении." : null;
    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);
    private static bool IsAuthenticated(HttpContext http) => http.User.Identity?.IsAuthenticated == true;
    private static RequestMetadata ToRequestMetadata(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
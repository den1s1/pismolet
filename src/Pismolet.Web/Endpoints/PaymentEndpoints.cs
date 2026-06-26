using System.Globalization;
using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/payment", ShowPayment).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/payment/fake-start", StartPayment).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/payment/fake-success", ConfirmPayment).RequireAuthorization();
        app.MapMethods("/payments/robokassa/result", new[] { "GET", "POST" }, RobokassaResult);
        app.MapMethods("/payments/robokassa/success", new[] { "GET", "POST" }, RobokassaSuccess);
        app.MapMethods("/payments/robokassa/fail", new[] { "GET", "POST" }, RobokassaFail);
        app.MapPost("/payments/robokassa/fake/checkout", FakeRobokassaCheckout);
        app.MapPost("/payments/robokassa/fake/success", FakeRobokassaSuccess);
        app.MapPost("/payments/robokassa/fake/fail", FakeRobokassaFail);
        return app;
    }

    private static IResult ShowPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = payments.GetPaymentReview(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(result), authenticated: true));
    }

    private static async Task<IResult> StartPayment(Guid id, HttpContext http, IMailingPaymentService payments, RobokassaPaymentOptions robokassa, PublicUrlOptions publicUrl)
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
        if (!result.Ok || result.Review?.Payment is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(result), authenticated: true));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Оплата через Robokassa", RobokassaRequestPage(result.Review, robokassa, publicUrl), authenticated: true));
    }

    private static async Task<IResult> ConfirmPayment(Guid id, HttpContext http, IMailingPaymentService payments)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var form = await http.Request.ReadFormAsync();
        var operationId = form["operationId"].ToString();
        var result = payments.ConfirmPayment(email, id, operationId, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Расчёт и оплата", PaymentPage(result), authenticated: true));
    }

    private static async Task<IResult> RobokassaResult(HttpContext http, IMailingPaymentService payments, IPaymentRepository paymentRepository, RobokassaPaymentOptions robokassa)
    {
        var fields = await ReadRobokassaFields(http);
        if (!TryValidateRobokassaSignature(fields, robokassa, RobokassaSignaturePurpose.Result, out var validation))
        {
            return Results.BadRequest(validation.Error);
        }

        var amountError = ValidatePaymentAmount(paymentRepository, validation.InvId, validation.OutSum);
        if (amountError is not null)
        {
            return Results.BadRequest(amountError);
        }

        var result = payments.ConfirmProviderPayment(validation.InvId, ToRequestMetadata(http), RawFields(fields));
        return result.Ok
            ? Results.Text($"OK{validation.InvId}", "text/plain; charset=utf-8")
            : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> RobokassaSuccess(HttpContext http, IPaymentRepository paymentRepository, RobokassaPaymentOptions robokassa, IMailingReviewService reviews)
    {
        var fields = await ReadRobokassaFields(http);
        if (fields.Count == 0)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Успешная оплата", RobokassaSuccessPage(null, string.Empty, "Страница SuccessURL готова к подключению в кабинете Robokassa.")));
        }

        if (!TryValidateRobokassaSignature(fields, robokassa, RobokassaSignaturePurpose.Success, out var validation))
        {
            return Results.BadRequest(validation.Error);
        }

        var amountError = ValidatePaymentAmount(paymentRepository, validation.InvId, validation.OutSum);
        if (amountError is not null)
        {
            return Results.BadRequest(amountError);
        }

        var payment = paymentRepository.GetByProviderOperationId(validation.InvId);
        if (payment?.Status == PaymentStatus.Paid)
        {
            reviews.StartChecks(payment.OwnerEmail, payment.MailingId, ToRequestMetadata(http));
            return Results.Redirect($"/mailings/{payment.MailingId}/send");
        }

        var message = payment?.Status == PaymentStatus.Paid
            ? "Оплата подтверждена серверным уведомлением ResultURL."
            : "Переход после оплаты получен. Окончательный статус меняет только ResultURL.";
        return HtmlRenderer.Html(HtmlRenderer.Page("Успешная оплата", RobokassaSuccessPage(payment, validation.InvId, message)));
    }

    private static async Task<IResult> RobokassaFail(HttpContext http, IPaymentRepository paymentRepository)
    {
        var fields = await ReadRobokassaFields(http);
        var invId = fields.GetValueOrDefault("InvId", string.Empty);
        var payment = string.IsNullOrWhiteSpace(invId) ? null : paymentRepository.GetByProviderOperationId(invId);
        return HtmlRenderer.Html(HtmlRenderer.Page("Оплата не завершена", RobokassaFailPage(payment, invId)));
    }

    private static async Task<IResult> FakeRobokassaCheckout(HttpContext http, IPaymentRepository paymentRepository, RobokassaPaymentOptions robokassa, PublicUrlOptions publicUrl)
    {
        var fields = await ReadRobokassaFields(http);
        if (!TryValidateRobokassaSignature(fields, robokassa, RobokassaSignaturePurpose.Start, out var validation))
        {
            return Results.BadRequest(validation.Error);
        }

        var amountError = ValidatePaymentAmount(paymentRepository, validation.InvId, validation.OutSum);
        if (amountError is not null)
        {
            return Results.BadRequest(amountError);
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Тестовый модуль Robokassa", FakeRobokassaCheckoutPage(fields, paymentRepository, publicUrl)));
    }

    private static async Task<IResult> FakeRobokassaSuccess(HttpContext http, IMailingPaymentService payments, IPaymentRepository paymentRepository, RobokassaPaymentOptions robokassa, IMailingReviewService reviews)
    {
        var startFields = await ReadRobokassaFields(http);
        if (!TryValidateRobokassaSignature(startFields, robokassa, RobokassaSignaturePurpose.Start, out var validation))
        {
            return Results.BadRequest(validation.Error);
        }

        var amountError = ValidatePaymentAmount(paymentRepository, validation.InvId, validation.OutSum);
        if (amountError is not null)
        {
            return Results.BadRequest(amountError);
        }

        var resultFields = BuildRobokassaCallbackFields(startFields, robokassa, RobokassaSignaturePurpose.Result);
        var result = payments.ConfirmProviderPayment(validation.InvId, ToRequestMetadata(http), RawFields(resultFields));
        if (!result.Ok)
        {
            return Results.BadRequest(result.Error);
        }

        var payment = paymentRepository.GetByProviderOperationId(validation.InvId);
        if (payment is not null)
        {
            reviews.StartChecks(payment.OwnerEmail, payment.MailingId, ToRequestMetadata(http));
            return Results.Redirect($"/mailings/{payment.MailingId}/send");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Успешная оплата", RobokassaSuccessPage(payment, validation.InvId, $"ResultURL вернул OK{validation.InvId}. Оплата подтверждена.")));
    }

    private static async Task<IResult> FakeRobokassaFail(HttpContext http, IPaymentRepository paymentRepository, RobokassaPaymentOptions robokassa)
    {
        var fields = await ReadRobokassaFields(http);
        if (!TryValidateRobokassaSignature(fields, robokassa, RobokassaSignaturePurpose.Start, out var validation))
        {
            return Results.BadRequest(validation.Error);
        }

        var payment = paymentRepository.GetByProviderOperationId(validation.InvId);
        return HtmlRenderer.Html(HtmlRenderer.Page("Оплата не завершена", RobokassaFailPage(payment, validation.InvId)));
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
        var isPromo = mailing.MessageDraft?.MessageType == MessageType.Advertising;
        var hasAdvertisingConsent = mailing.Declaration?.IsAdvertisingConsentConfirmed == true;
        var alert = string.IsNullOrWhiteSpace(confirmationError) ? string.Empty : $"<p class='error-message'>{H(confirmationError)}</p>";
        var paymentRulesHref = $"/legal/payment-and-refund?returnUrl=/mailings/{mailing.Id}/payment";
        var payButtonText = $"Оплатить {review.TotalAmount:0.##} ₽";
        var button = paid
            ? $"<p><span class='badge'>Оплачено</span></p><form method='post' action='/mailings/{mailing.Id}/checks/start'><button class='button'>Перейти к запуску</button></form><p><a href='/mailings/{mailing.Id}/send'>Открыть запуск рассылки</a></p>"
            : PaymentAction(mailing, isPromo, hasAdvertisingConsent, paymentRulesHref, payButtonText);

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
    <div class='stats payment-stats payment-key-stats'>
      <div class='stat'><b>{stats.Accepted}</b><span>принято к отправке</span></div>
      <div class='stat'><b>{excluded}</b><span>исключено из расчёта</span></div>
      <div class='stat'><b>{review.TotalAmount:0.##} ₽</b><span>к оплате</span></div>
    </div>
    <section class='box payment-legal-summary'>
      <h2>Подтверждения базы</h2>
      <p class='muted'>Юридические подтверждения уже сохранены на шаге адресов. На оплате они показаны только для проверки.</p>
      <div class='payment-summary-grid'>
        <div><b>Источник базы</b><p>{H(mailing.Declaration?.BaseSource.ToRu() ?? "не подтверждён")}</p></div>
        <div><b>Тип письма</b><p>{H((mailing.MessageDraft?.MessageType ?? MessageType.Transactional).ToRu())}</p></div>
        <div><b>Правомерность базы</b><p>{(mailing.Declaration?.IsBaseLegalityConfirmed == true ? "подтверждена" : "не подтверждена")}</p></div>
        <div><b>Рекламное согласие</b><p>{AdvertisingConsentStatus(isPromo, hasAdvertisingConsent)}</p></div>
      </div>
    </section>
    <div class='payment-grid'>
      <section class='box confirmation-card'>{button}</section>
      <section class='box cost-card pay-card'>
        <div class='pay-summary-line'><small>К оплате</small><strong class='sum'>{review.TotalAmount:0.##} ₽</strong></div>
        <p>{stats.Accepted} письмо × {review.PricePerRecipient:0.##} ₽. За исключённые {excluded} адрес не платите.</p>
        <p class='muted'>Правила оплаты, запуска и возвратов: <a href='{paymentRulesHref}'>открыть документ</a>.</p>
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

    private static string PaymentAction(Mailing mailing, bool isPromo, bool hasAdvertisingConsent, string paymentRulesHref, string payButtonText)
    {
        if (isPromo && !hasAdvertisingConsent)
        {
            return $"<h2>Нужно подтвердить рекламное согласие</h2><p class='notice warn'>Это рекламная рассылка. Вернитесь на шаг адресов и подтвердите наличие рекламного согласия адресатов.</p><a class='button' href='/mailings/{mailing.Id}/recipients'>Вернуться к адресам</a>";
        }

        return $"<form method='post' action='/mailings/{mailing.Id}/payment/fake-start' class='confirmation-list checks'><h2>Финальное подтверждение</h2><label class='check'><input type='checkbox' name='campaignLaunchConfirmation'><span>Я проверил рассылку, понимаю сумму к оплате и условия запуска после оплаты и проверок. <a href='{paymentRulesHref}'>Правила оплаты, запуска и возвратов</a>.</span></label><div class='notice warn'>Если рассылка не будет отправлена по технической причине или из-за отказа Письмолёта до начала отправки, вопрос возврата решается по правилам возврата.</div><button class='button full-pay-button'>{H(payButtonText)}</button><p class='muted payment-provider-note'>После подтверждения откроется платёжная страница.</p></form>";
    }

    private static string AdvertisingConsentStatus(bool isPromo, bool hasAdvertisingConsent) => isPromo
        ? hasAdvertisingConsent ? "подтверждено" : "не подтверждено"
        : "не требуется";

    private static string RobokassaRequestPage(MailingPaymentReview review, RobokassaPaymentOptions robokassa, PublicUrlOptions publicUrl)
    {
        var payment = review.Payment ?? throw new InvalidOperationException("Payment is required after StartPayment.");
        var fields = BuildRobokassaStartFields(review, payment, robokassa);
        var resultUrl = AbsoluteUrl(publicUrl, "/payments/robokassa/result");
        var successUrl = AbsoluteUrl(publicUrl, "/payments/robokassa/success");
        var failUrl = AbsoluteUrl(publicUrl, "/payments/robokassa/fail");

        return $@"
<section class='wizard-shell payment-wizard'>
  <section class='panel'>
    <p class='eyebrow'>Тестовый платёжный модуль</p>
    <h1>Оплата через Robokassa</h1>
    <p class='muted'>Форма использует те же ключевые поля, которые понадобятся при подключении настоящего магазина Robokassa.</p>
    <div class='payment-grid'>
      <section class='box confirmation-card'>
        <h2>Параметры платежа</h2>
        <dl class='cost-list'>
          <div><dt>MerchantLogin</dt><dd>{H(fields["MerchantLogin"])}</dd></div>
          <div><dt>OutSum</dt><dd>{H(fields["OutSum"])}</dd></div>
          <div><dt>InvId</dt><dd>{H(fields["InvId"])}</dd></div>
          <div><dt>IsTest</dt><dd>{H(fields.GetValueOrDefault("IsTest", "0"))}</dd></div>
        </dl>
        <form method='post' action='{H(robokassa.PaymentUrl)}'>
          {HiddenFields(fields)}
          <button class='button full-pay-button'>Перейти к оплате в тестовый модуль Robokassa</button>
        </form>
      </section>
      <section class='box cost-card pay-card'>
        <h2>URL для кабинета Robokassa</h2>
        <dl class='cost-list'>
          <div><dt>ResultURL</dt><dd>{H(resultUrl)}</dd></div>
          <div><dt>SuccessURL</dt><dd>{H(successUrl)}</dd></div>
          <div><dt>FailURL</dt><dd>{H(failUrl)}</dd></div>
        </dl>
        <p class='muted'>Статус заказа меняет только ResultURL после проверки подписи и суммы. SuccessURL показывает пользователю итоговый экран.</p>
      </section>
    </div>
    <div class='actions'><a class='btn secondary' href='/mailings/{review.Mailing.Id}/payment'>Вернуться к расчёту</a></div>
  </section>
</section>";
    }

    private static string FakeRobokassaCheckoutPage(IReadOnlyDictionary<string, string> fields, IPaymentRepository paymentRepository, PublicUrlOptions publicUrl)
    {
        var invId = fields["InvId"];
        var payment = paymentRepository.GetByProviderOperationId(invId);
        var mailingLink = payment is null ? string.Empty : $"<p><a href='/mailings/{payment.MailingId}/payment'>Вернуться к оплате рассылки</a></p>";

        return $@"
<section class='panel'>
  <p class='eyebrow'>Robokassa sandbox</p>
  <h1>Тестовый модуль Robokassa</h1>
  <p class='muted'>Эта страница имитирует внешнюю платёжную форму. Она принимает поля MerchantLogin, OutSum, InvId, SignatureValue и возвращает пользователя через ResultURL/SuccessURL/FailURL.</p>
  <dl class='cost-list'>
    <div><dt>Магазин</dt><dd>{H(fields["MerchantLogin"])}</dd></div>
    <div><dt>Сумма</dt><dd>{H(fields["OutSum"])} ₽</dd></div>
    <div><dt>InvId</dt><dd>{H(invId)}</dd></div>
    <div><dt>ResultURL</dt><dd>{H(AbsoluteUrl(publicUrl, "/payments/robokassa/result"))}</dd></div>
    <div><dt>SuccessURL</dt><dd>{H(AbsoluteUrl(publicUrl, "/payments/robokassa/success"))}</dd></div>
    <div><dt>FailURL</dt><dd>{H(AbsoluteUrl(publicUrl, "/payments/robokassa/fail"))}</dd></div>
  </dl>
  <div class='actions'>
    <form method='post' action='/payments/robokassa/fake/success'>
      {HiddenFields(fields)}
      <button class='btn'>Оплатить успешно</button>
    </form>
    <form method='post' action='/payments/robokassa/fake/fail'>
      {HiddenFields(fields)}
      <button class='btn secondary'>Отменить оплату</button>
    </form>
  </div>
  {mailingLink}
</section>";
    }

    private static string RobokassaSuccessPage(Payment? payment, string invId, string message)
    {
        var title = payment?.Status == PaymentStatus.Paid ? "Оплата подтверждена" : "Переход после оплаты получен";
        var next = payment is null
            ? "<p><a class='btn secondary' href='/'>На главную</a></p>"
            : payment.Status == PaymentStatus.Paid
                ? $"<form method='post' action='/mailings/{payment.MailingId}/checks/start'><button class='btn'>Перейти к запуску</button></form><p><a href='/mailings/{payment.MailingId}/send'>Открыть запуск рассылки</a></p>"
                : $@"<div class='notice warn'>Мы получили возврат из платёжного модуля и ждём серверное подтверждение оплаты. Обычно оно приходит быстро, но финальный статус меняет только ResultURL.</div>
  <p><a class='btn' href='/mailings/{payment.MailingId}/send'>Проверить статус и продолжить</a></p>
  <p><a href='/dashboard'>В личный кабинет</a></p>";
        var inv = string.IsNullOrWhiteSpace(invId) ? string.Empty : $"<p class='muted'>InvId: {H(invId)}</p>";

        return $@"
<section class='panel'>
  <h1>{H(title)}</h1>
  <p>{H(message)}</p>
  {inv}
  {next}
</section>";
    }

    private static string RobokassaFailPage(Payment? payment, string invId)
    {
        var retry = payment is null
            ? "<p><a class='btn secondary' href='/dashboard'>В личный кабинет</a></p>"
            : $"<p><a class='btn' href='/mailings/{payment.MailingId}/payment'>Повторить оплату</a></p>";
        var inv = string.IsNullOrWhiteSpace(invId) ? string.Empty : $"<p class='muted'>InvId: {H(invId)}</p>";

        return $@"
<section class='panel'>
  <h1>Оплата не завершена</h1>
  <p>Платёжный модуль вернул пользователя без успешного платежа. Статус рассылки не менялся.</p>
  {inv}
  {retry}
</section>";
    }

    private static Dictionary<string, string> BuildRobokassaStartFields(MailingPaymentReview review, Payment payment, RobokassaPaymentOptions robokassa)
    {
        var invId = payment.Attempts.LastOrDefault()?.ProviderOperationId ?? RobokassaPaymentModule.BuildInvId(payment.Id);
        var outSum = RobokassaPaymentModule.FormatAmount(payment.TotalAmount);
        var shp = RobokassaPaymentModule.ShpMailing(payment.MailingId);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantLogin"] = robokassa.MerchantLogin,
            ["OutSum"] = outSum,
            ["InvId"] = invId,
            ["Description"] = $"Оплата рассылки {review.Mailing.PublicId}",
            ["Email"] = payment.OwnerEmail,
            ["Culture"] = "ru",
            ["Encoding"] = "utf-8"
        };

        foreach (var parameter in shp)
        {
            fields[parameter.Key] = parameter.Value;
        }

        if (robokassa.IsTest)
        {
            fields["IsTest"] = "1";
        }

        fields["SignatureValue"] = RobokassaPaymentModule.BuildStartSignature(robokassa.MerchantLogin, outSum, invId, robokassa.Password1, shp);
        return fields;
    }

    private static Dictionary<string, string> BuildRobokassaCallbackFields(IReadOnlyDictionary<string, string> startFields, RobokassaPaymentOptions robokassa, RobokassaSignaturePurpose purpose)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OutSum"] = startFields["OutSum"],
            ["InvId"] = startFields["InvId"]
        };

        foreach (var parameter in ExtractShpFields(startFields))
        {
            fields[parameter.Key] = parameter.Value;
        }

        fields["SignatureValue"] = purpose == RobokassaSignaturePurpose.Result
            ? RobokassaPaymentModule.BuildResultSignature(fields["OutSum"], fields["InvId"], robokassa.Password2, ExtractShpFields(fields))
            : RobokassaPaymentModule.BuildSuccessSignature(fields["OutSum"], fields["InvId"], robokassa.Password1, ExtractShpFields(fields));
        return fields;
    }

    private static async Task<Dictionary<string, string>> ReadRobokassaFields(HttpContext http)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in http.Request.Query)
        {
            fields[item.Key] = item.Value.ToString();
        }

        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync();
            foreach (var item in form)
            {
                fields[item.Key] = item.Value.ToString();
            }
        }

        return fields;
    }

    private static bool TryValidateRobokassaSignature(IReadOnlyDictionary<string, string> fields, RobokassaPaymentOptions robokassa, RobokassaSignaturePurpose purpose, out RobokassaSignatureValidation validation)
    {
        if (!fields.TryGetValue("OutSum", out var outSum) || string.IsNullOrWhiteSpace(outSum))
        {
            validation = RobokassaSignatureValidation.Failure("Не передан параметр OutSum.");
            return false;
        }

        if (!fields.TryGetValue("InvId", out var invId) || string.IsNullOrWhiteSpace(invId))
        {
            validation = RobokassaSignatureValidation.Failure("Не передан параметр InvId.");
            return false;
        }

        if (!fields.TryGetValue("SignatureValue", out var signature) || string.IsNullOrWhiteSpace(signature))
        {
            validation = RobokassaSignatureValidation.Failure("Не передан параметр SignatureValue.");
            return false;
        }

        var shp = ExtractShpFields(fields);
        var expected = purpose switch
        {
            RobokassaSignaturePurpose.Start => BuildExpectedStartSignature(fields, robokassa, outSum, invId, shp),
            RobokassaSignaturePurpose.Result => RobokassaPaymentModule.BuildResultSignature(outSum, invId, robokassa.Password2, shp),
            _ => RobokassaPaymentModule.BuildSuccessSignature(outSum, invId, robokassa.Password1, shp)
        };

        if (purpose == RobokassaSignaturePurpose.Start && string.IsNullOrWhiteSpace(expected))
        {
            validation = RobokassaSignatureValidation.Failure("Некорректный MerchantLogin.");
            return false;
        }

        if (!RobokassaPaymentModule.Verify(signature, expected))
        {
            validation = RobokassaSignatureValidation.Failure("Некорректная подпись Robokassa.");
            return false;
        }

        validation = RobokassaSignatureValidation.Success(outSum, invId);
        return true;
    }

    private static string BuildExpectedStartSignature(IReadOnlyDictionary<string, string> fields, RobokassaPaymentOptions robokassa, string outSum, string invId, IReadOnlyDictionary<string, string> shp)
    {
        if (!fields.TryGetValue("MerchantLogin", out var merchantLogin) || !string.Equals(merchantLogin, robokassa.MerchantLogin, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return RobokassaPaymentModule.BuildStartSignature(merchantLogin, outSum, invId, robokassa.Password1, shp);
    }

    private static string? ValidatePaymentAmount(IPaymentRepository paymentRepository, string invId, string outSum)
    {
        var payment = paymentRepository.GetByProviderOperationId(invId);
        if (payment is null)
        {
            return "Платёжная попытка не найдена.";
        }

        if (!decimal.TryParse(outSum, NumberStyles.Number, CultureInfo.InvariantCulture, out var actual))
        {
            return "Некорректная сумма платежа.";
        }

        return actual == payment.TotalAmount
            ? null
            : "Сумма платежа не совпадает с заказом.";
    }

    private static SortedDictionary<string, string> ExtractShpFields(IReadOnlyDictionary<string, string> fields)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field.Key.StartsWith("Shp_", StringComparison.Ordinal))
            {
                result[field.Key] = field.Value;
            }
        }

        return result;
    }

    private static string HiddenFields(IReadOnlyDictionary<string, string> fields) =>
        string.Concat(fields.OrderBy(field => field.Key, StringComparer.Ordinal).Select(field => $"<input type='hidden' name='{H(field.Key)}' value='{H(field.Value)}'>"));

    private static string RawFields(IReadOnlyDictionary<string, string> fields) =>
        string.Join(';', fields.OrderBy(field => field.Key, StringComparer.Ordinal).Select(field => $"{field.Key}={field.Value}"));

    private static string AbsoluteUrl(PublicUrlOptions publicUrl, string path) => $"{publicUrl.PublicBaseUrl}{path}";

    private static string? ValidatePaymentConfirmations(Mailing mailing, IFormCollection form)
    {
        if (!form.ContainsKey("campaignLaunchConfirmation")) return "Подтвердите финальный запуск и правила оплаты.";
        if (mailing.MessageDraft?.MessageType == MessageType.Advertising && mailing.Declaration?.IsAdvertisingConsentConfirmed != true)
        {
            return "Для рекламной рассылки сначала подтвердите рекламное согласие адресатов на шаге адресов.";
        }

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

    private enum RobokassaSignaturePurpose
    {
        Start,
        Result,
        Success
    }

    private sealed record RobokassaSignatureValidation(bool Ok, string Error, string OutSum, string InvId)
    {
        public static RobokassaSignatureValidation Success(string outSum, string invId) => new(true, string.Empty, outSum, invId);

        public static RobokassaSignatureValidation Failure(string error) => new(false, error, string.Empty, string.Empty);
    }
}

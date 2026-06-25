using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class SimplifiedRecipientStepEndpoints
{
    public static IEndpointRouteBuilder MapSimplifiedRecipientStepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/recipients", Show).RequireAuthorization().WithOrder(-200);
        return app;
    }

    private static IResult Show(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = http.User.FindFirstValue(ClaimTypes.Email);
        var mailing = email is null ? null : mailings.GetForOwner(id, email);
        return mailing is null
            ? HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true))
            : HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", Page(mailing), authenticated: true));
    }

    private static string Page(Mailing mailing)
    {
        var options = string.Join("", BaseSourceLabels.All.Select(x => $"<option value='{x.Key}'>{H(x.Value)}</option>"));
        return """
<section class='wizard-shell'>
  <div class='wizard-steps'><span class='wizard-step current'>1. Адреса</span><span class='wizard-step'>2. Письмо</span><span class='wizard-step'>3. Расчёт и оплата</span><span class='wizard-step'>4. Готово</span></div>
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 4</p>
    <h1>1. Добавьте список адресов</h1>
    <p class='muted'>Не используйте купленные или чужие базы.</p>
    <form method='post' action='/mailings/__ID__/recipients' enctype='multipart/form-data' class='simple-recipient-form'>
      <div class='wizard-grid'>
        <label class='dropzone'><span>Перетащите таблицу Excel сюда</span><small>или нажмите, чтобы выбрать файл.</small><input type='file' name='file' accept='.csv,.xlsx'></label>
        <label class='manual-addresses'><span>Или вставьте адреса вручную</span><small>Каждый адрес — с новой строки.</small><textarea name='manualAddresses' rows='12' placeholder='anna@example.ru&#10;club@example.ru&#10;ivan@example.ru'></textarea></label>
      </div>
      <section class='inline-base-confirm'>
        <h2>Подтвердите базу</h2>
        <div class='compact-base-fields'>
          <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>__OPTIONS__</select></label>
          <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select></label>
        </div>
        <label class='compact-base-check'><input type='checkbox' name='baseLegality'><span>подтверждаю правомерность использования базы</span></label>
        <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'><span>подтверждаю наличие рекламного согласия адресатов</span></label>
        <p class='compact-legal-link'><a href='/legal/base-lawfulness?returnUrl=/mailings/__ID__/recipients'>Декларация законности базы</a></p>
      </section>
      <div class='actions wizard-actions'><button class='button'>Адреса добавлены, дальше</button><a class='btn secondary' href='/dashboard'>Вернуться в ЛК</a></div>
    </form>
  </section>
</section>
""".Replace("__ID__", mailing.Id.ToString(), StringComparison.Ordinal).Replace("__OPTIONS__", options, StringComparison.Ordinal);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

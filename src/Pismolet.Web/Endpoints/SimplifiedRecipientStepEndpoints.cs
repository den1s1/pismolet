using System.Net;
using System.Reflection;
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
        var sourceValue = DeclarationValue(mailing.Declaration, "BaseSource");
        var typeValue = DeclarationValue(mailing.Declaration, "IntendedMessageType")
            ?? DeclarationValue(mailing.Declaration, "MessageType")
            ?? mailing.MessageDraft?.MessageType.ToString()
            ?? MessageType.Transactional.ToString();
        var options = string.Join("", BaseSourceLabels.All.Select(x => Option(x.Key.ToString(), x.Value, sourceValue)));
        var stats = mailing.LastImportStats.TotalRows > 0 ? Stats(mailing) : string.Empty;
        var baseChecked = mailing.Declaration is null ? string.Empty : " checked";
        var adChecked = IsTrue(mailing.Declaration, "IsAdvertisingConsentConfirmed", "AdvertisingConsentConfirmed", "HasAdvertisingConsent") ? " checked" : string.Empty;
        var txSelected = typeValue == MessageType.Advertising.ToString() ? string.Empty : " selected";
        var adSelected = typeValue == MessageType.Advertising.ToString() ? " selected" : string.Empty;
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
      __STATS__
      <section class='inline-base-confirm'>
        <h2>Подтвердите базу</h2>
        <div class='compact-base-fields'>
          <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>__OPTIONS__</select></label>
          <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'__TX__>Информационное</option><option value='Advertising'__AD__>Рекламное</option></select></label>
        </div>
        <label class='compact-base-check'><input type='checkbox' name='baseLegality'__BASE_CHECKED__><span>подтверждаю правомерность использования базы</span></label>
        <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'__AD_CHECKED__><span>подтверждаю наличие рекламного согласия адресатов</span></label>
        <p class='compact-legal-link'><a href='/legal/base-lawfulness?returnUrl=/mailings/__ID__/recipients'>Декларация законности базы</a></p>
      </section>
      <div class='actions wizard-actions'><button class='button'>Адреса добавлены, дальше</button><a class='btn secondary' href='/dashboard'>Вернуться в ЛК</a></div>
    </form>
  </section>
</section>
"""
            .Replace("__ID__", mailing.Id.ToString(), StringComparison.Ordinal)
            .Replace("__STATS__", stats, StringComparison.Ordinal)
            .Replace("__OPTIONS__", options, StringComparison.Ordinal)
            .Replace("__TX__", txSelected, StringComparison.Ordinal)
            .Replace("__AD__", adSelected, StringComparison.Ordinal)
            .Replace("__BASE_CHECKED__", baseChecked, StringComparison.Ordinal)
            .Replace("__AD_CHECKED__", adChecked, StringComparison.Ordinal);
    }

    private static string Stats(Mailing mailing)
    {
        var stats = mailing.LastImportStats;
        var blocked = stats.Invalid + stats.Duplicates + stats.GloballySuppressed + stats.ClientSuppressed;
        return $"<div class='stats import-summary'><div class='stat'><b>{stats.TotalRows}</b><span>Строк в файле</span></div><div class='stat'><b>{stats.Accepted}</b><span>Принято к отправке</span></div><div class='stat'><b>{stats.Duplicates + stats.Invalid}</b><span>Дублей и ошибок</span></div><div class='stat'><b>{blocked}</b><span>Не сможем отправить</span></div><div class='stat'><b>{stats.GloballySuppressed}</b><span>Ранее отписались</span></div></div>";
    }

    private static string Option(string value, string label, string? selectedValue)
    {
        var selected = string.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
        return $"<option value='{H(value)}'{selected}>{H(label)}</option>";
    }

    private static string? DeclarationValue(object? declaration, params string[] names)
    {
        if (declaration is null) return null;
        var type = declaration.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var value = property?.GetValue(declaration)?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static bool IsTrue(object? declaration, params string[] names)
    {
        if (declaration is null) return false;
        var type = declaration.GetType();
        foreach (var name in names)
        {
            if (type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(declaration) is true) return true;
        }

        return false;
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

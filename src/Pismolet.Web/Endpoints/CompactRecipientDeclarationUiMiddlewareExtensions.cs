using System.Text;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Endpoints;

public static class CompactRecipientDeclarationUiMiddlewareExtensions
{
    private static readonly Regex DeclarationFormRegex = new(
        @"<form method='post' action='(?<action>[^']+/declaration)' class='form-grid confirmation-list'>\s*<label>Источник базы<select name='baseSource' required><option value=''>Выберите источник</option>(?<sources>.*?)</select></label>\s*<label>Тип письма<select name='messageType'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select></label>\s*<label class='check'><input type='checkbox' name='baseLegality'><span>.*?</span></label>\s*<label class='check'><input type='checkbox' name='advertisingConsent'><span>.*?</span></label>\s*<button class='button'>Перейти к письму</button>\s*</form>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex LegalBoxRegex = new(
        @"\s*<section class='box muted-box'>\s*<h2>Декларация законности базы</h2>\s*<p>Полный текст вынесен в отдельный юридический документ\.</p>\s*<a class='btn secondary' href='(?<href>[^']+)'>Открыть декларацию</a>\s*</section>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static IApplicationBuilder UseCompactRecipientDeclarationUi(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!ShouldTransform(context.Request.Path))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next();

        buffer.Position = 0;
        if (context.Response.StatusCode != StatusCodes.Status200OK || !IsHtml(context.Response.ContentType))
        {
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
            return;
        }

        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var html = await reader.ReadToEndAsync();
        var transformed = Transform(html, context.Request.Path.Value ?? string.Empty);
        var bytes = Encoding.UTF8.GetBytes(transformed);

        context.Response.Body = originalBody;
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    });

    private static bool ShouldTransform(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.StartsWith("/mailings/", StringComparison.OrdinalIgnoreCase)
            && value.EndsWith("/recipients", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtml(string? contentType) =>
        contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

    private static string Transform(string html, string recipientsPath)
    {
        if (!html.Contains("Подтвердите базу", StringComparison.Ordinal) || !html.Contains("name='messageType'", StringComparison.Ordinal))
        {
            return html;
        }

        var returnUrl = string.IsNullOrWhiteSpace(recipientsPath) ? "/dashboard" : recipientsPath;
        var legalHref = $"/legal/base-lawfulness?returnUrl={returnUrl}";

        html = LegalBoxRegex.Replace(html, string.Empty);
        html = DeclarationFormRegex.Replace(html, match => CompactForm(match.Groups["action"].Value, match.Groups["sources"].Value, legalHref));
        html = html.Replace("<div class='split-grid'>", "<div class='compact-base-section'>", StringComparison.Ordinal);
        html = html.Replace("<section class='box'>\n        <h2>Подтвердите базу</h2>", "<section class='compact-base-card'>\n        <h2>Подтвердите базу</h2>", StringComparison.Ordinal);
        html = html.Replace("<p class='muted'>Источник и подтверждения фиксируются вместе с этим шагом.</p>", "<p class='muted'>Источник и подтверждения фиксируются вместе с этим шагом.</p>", StringComparison.Ordinal);

        if (!html.Contains("compact-ad-consent-script", StringComparison.Ordinal))
        {
            html = html.Replace("</section>\n</section>", "</section>\n</section>" + AdvertisingConsentScript(), StringComparison.Ordinal);
        }

        return html;
    }

    private static string CompactForm(string action, string sourceOptions, string legalHref) => $@"
<form method='post' action='{action}' class='compact-base-form'>
  <div class='compact-base-fields'>
    <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>{sourceOptions}</select></label>
    <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select></label>
  </div>
  <label class='compact-base-check'><input type='checkbox' name='baseLegality'><span>подтверждаю правомерность использования базы</span></label>
  <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'><span>подтверждаю наличие рекламного согласия адресатов</span></label>
  <p class='compact-legal-link'><a href='{legalHref}'>Декларация законности базы</a></p>
  <button class='button'>Перейти к письму</button>
</form>";

    private static string AdvertisingConsentScript() => @"
<script class='compact-ad-consent-script'>
(function () {
  var select = document.getElementById('messageTypeSelect');
  var block = document.getElementById('advertisingConsentBlock');
  if (!select || !block) return;
  function syncAdvertisingConsent() {
    block.style.display = select.value === 'Advertising' ? 'flex' : 'none';
  }
  select.addEventListener('change', syncAdvertisingConsent);
  syncAdvertisingConsent();
})();
</script>";
}

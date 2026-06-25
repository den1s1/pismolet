using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class CompactRecipientDeclarationUiMiddlewareExtensions
{
    private static readonly Regex DeclarationFormRegex = new(
        @"<form method='post' action='(?<action>[^']+/declaration)' class='form-grid confirmation-list'>.*?</form>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex SourceOptionsRegex = new(
        @"<select name='baseSource' required><option value=''>Выберите источник</option>(?<sources>.*?)</select>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex LegalBoxRegex = new(
        @"\s*<section class='box muted-box'>\s*<h2>Декларация законности базы</h2>\s*<p>Полный текст вынесен в отдельный юридический документ\.</p>\s*<a class='btn secondary' href='(?<href>[^']+)'>Открыть декларацию</a>\s*</section>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex ExcludedBlockRegex = new(
        @"\s*<h2>Что исключено</h2>\s*(?:<p class='muted'>Исключённых адресов нет\.</p>|<ul class='issue-list'>.*?</ul>)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex BaseCardStartRegex = new(
        @"<section class='box'>\s*<h2>Подтвердите базу</h2>",
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

        if (await TryRedirectAfterImport(context))
        {
            context.Response.Body = originalBody;
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = $"/mailings/{ExtractMailingId(context.Request.Path)}/message";
            context.Response.ContentLength = 0;
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
        var legalHref = $"/legal/base-lawfulness?returnUrl={(string.IsNullOrWhiteSpace(recipientsPath) ? "/dashboard" : recipientsPath)}";

        if (html.Contains("name='manualAddresses'", StringComparison.Ordinal) && !html.Contains("name='baseSource'", StringComparison.Ordinal))
        {
            html = html.Replace("<div class='actions wizard-actions'>", InitialControls(legalHref) + "\n      <div class='actions wizard-actions'>", StringComparison.Ordinal);
        }

        if (!html.Contains("Подтвердите базу", StringComparison.Ordinal) || !html.Contains("name='messageType'", StringComparison.Ordinal))
        {
            return AddScript(html);
        }

        html = html.Replace("<h1>Адреса проверены</h1>", "<h1>1. Добавьте список адресов</h1>", StringComparison.Ordinal);
        html = ExcludedBlockRegex.Replace(html, string.Empty);
        html = LegalBoxRegex.Replace(html, string.Empty);
        html = DeclarationFormRegex.Replace(html, match => CompactForm(match.Groups["action"].Value, ExtractSourceOptions(match.Value), legalHref));
        html = html.Replace("<div class='split-grid'>", "<div class='compact-base-section' style='display:block;margin-top:18px;'>", StringComparison.Ordinal);
        html = BaseCardStartRegex.Replace(html, "<section class='compact-base-card' style='border:0;background:transparent;padding:0;box-shadow:none;'>\n        <h2>Подтвердите базу</h2>");
        html = html.Replace("<p class='muted'>Источник и подтверждения фиксируются вместе с этим шагом.</p>", string.Empty, StringComparison.Ordinal);
        return AddScript(html);
    }

    private static string AddScript(string html) => html.Contains("compact-ad-consent-script", StringComparison.Ordinal)
        ? html
        : html.Replace("</body>", AdvertisingConsentScript() + "\n</body>", StringComparison.OrdinalIgnoreCase);

    private static string ExtractSourceOptions(string formHtml)
    {
        var match = SourceOptionsRegex.Match(formHtml);
        return match.Success ? match.Groups["sources"].Value : string.Empty;
    }

    private static string InitialControls(string legalHref) => $@"
      <section class='inline-base-confirm' style='grid-column:1/-1;margin-top:8px;'>
        <h2>Подтвердите базу</h2>
        {Controls(legalHref, SourceOptions())}
      </section>";

    private static string CompactForm(string action, string sourceOptions, string legalHref) => $@"
<form method='post' action='{action}' class='compact-base-form' style='display:grid;gap:10px;margin-top:10px;max-width:620px;'>
  {Controls(legalHref, sourceOptions)}
  <button class='button' style='width:max-content;min-width:190px;margin-top:2px;'>Перейти к письму</button>
</form>";

    private static string SourceOptions() => string.Join("", BaseSourceLabels.All.Select(x => $"<option value='{x.Key}' style='font-weight:400;'>{x.Value}</option>"));

    private static string Controls(string legalHref, string sourceOptions) => $@"
  <div class='compact-base-fields' style='display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap;'>
    <label class='compact-base-field' style='display:grid;gap:5px;flex:0 0 280px;font-weight:400;border:0;background:transparent;padding:0;'><span style='font-weight:400;'>Источник базы</span><select name='baseSource' required style='width:100%;min-height:38px;padding:7px 10px;border-radius:12px;font-weight:400;'><option value='' style='font-weight:400;'>Выберите источник</option>{sourceOptions}</select></label>
    <label class='compact-base-field' style='display:grid;gap:5px;flex:0 0 220px;font-weight:400;border:0;background:transparent;padding:0;'><span style='font-weight:400;'>Тип письма</span><select name='messageType' id='messageTypeSelect' style='width:100%;min-height:38px;padding:7px 10px;border-radius:12px;font-weight:400;'><option value='Transactional' style='font-weight:400;'>Информационное</option><option value='Advertising' style='font-weight:400;'>Рекламное</option></select></label>
  </div>
  <label class='compact-base-check' style='display:flex;align-items:center;gap:8px;font-weight:400;border:0;background:transparent;padding:0;margin:0;'><input type='checkbox' name='baseLegality' style='width:16px;height:16px;min-width:16px;margin:0;'><span style='font-weight:400;'>подтверждаю правомерность использования базы</span></label>
  <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock' style='display:none;align-items:center;gap:8px;font-weight:400;border:0;background:transparent;padding:0;margin:0;'><input type='checkbox' name='advertisingConsent' style='width:16px;height:16px;min-width:16px;margin:0;'><span style='font-weight:400;'>подтверждаю наличие рекламного согласия адресатов</span></label>
  <p class='compact-legal-link' style='margin:2px 0 0;'><a href='{legalHref}' style='font-weight:400;'>Декларация законности базы</a></p>";

    private static async Task<bool> TryRedirectAfterImport(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !context.Request.HasFormContentType) return false;
        var id = ExtractMailingId(context.Request.Path);
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        if (id == Guid.Empty || email is null) return false;
        var form = await context.Request.ReadFormAsync();
        if (!form.ContainsKey("baseSource") || !form.ContainsKey("baseLegality")) return false;
        var type = TryParseMessageType(form["messageType"].ToString());
        if (type == MessageType.Advertising && !form.ContainsKey("advertisingConsent")) return false;
        var mailings = context.RequestServices.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null || mailing.LastImportStats.Accepted <= 0) return false;
        var declarations = context.RequestServices.GetRequiredService<IMailingDeclarationService>();
        var result = declarations.Confirm(new ConfirmMailingDeclarationCommand(email, id, TryParseBaseSource(form["baseSource"].ToString()), true, form.ContainsKey("advertisingConsent"), type, ToRequestMetadata(context)));
        return result.Ok;
    }

    private static Guid ExtractMailingId(PathString path)
    {
        var parts = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && Guid.TryParse(parts[1], out var id) ? id : Guid.Empty;
    }

    private static BaseSource? TryParseBaseSource(string value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;
    private static MessageType TryParseMessageType(string value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;
    private static RequestMetadata ToRequestMetadata(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
    private static string AdvertisingConsentScript() => "<script class='compact-ad-consent-script'>(function(){var s=document.getElementById('messageTypeSelect');var b=document.getElementById('advertisingConsentBlock');if(!s||!b)return;function x(){b.style.display=s.value==='Advertising'?'flex':'none';}s.addEventListener('change',x);x();})();</script>";
}

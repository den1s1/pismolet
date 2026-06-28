using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class MailingRichMessageFlowEndpoints
{
    private const string BodyFormatText = "text";
    private const string BodyFormatHtml = "html";
    private const string BodyTabVisual = "visual";
    private const string BodyTabHtml = "html";

    public static IEndpointRouteBuilder MapMailingRichMessageFlowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/message", ShowMessageEditor)
            .RequireAuthorization()
            .WithOrder(-2000);

        app.MapGet("/mailings/{id:guid}/message/preview", ShowMessagePreview)
            .RequireAuthorization()
            .WithOrder(-2000);

        app.MapPost("/mailings/{id:guid}/message", SaveMessage)
            .RequireAuthorization()
            .WithOrder(-2000);

        return app;
    }

    private static IResult ShowMessageEditor(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Письмо", MessageForm(mailing, null), authenticated: true));
    }

    private static IResult ShowMessagePreview(Guid id, HttpContext http, IMailingService mailings, IMessageRenderingService renderer)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        if (mailing.MessageDraft is null)
        {
            return Results.Redirect($"/mailings/{id}/message");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Предпросмотр письма", MessagePreviewPage(mailing, renderer), authenticated: true));
    }

    private static async Task<IResult> SaveMessage(Guid id, HttpContext http, IMailingService mailings, IMailingMessageService messages)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var existing = mailings.GetForOwner(id, email);
        if (existing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync();
        var senderName = form["senderName"].ToString();
        var subject = form["subject"].ToString();
        var bodyFormat = NormalizeBodyFormat(form["bodyFormat"].ToString());
        var bodyTab = NormalizeBodyTab(form["bodyTab"].ToString());
        var visualBody = form["visualBody"].ToString();
        var plainBody = form["plainBody"].ToString();
        var htmlBody = form["htmlBody"].ToString();
        var legacyBody = form["body"].ToString();

        if (!string.IsNullOrWhiteSpace(legacyBody))
        {
            if (bodyTab == BodyTabHtml && string.IsNullOrWhiteSpace(htmlBody))
            {
                htmlBody = legacyBody;
            }
            else if (bodyTab == BodyTabVisual && string.IsNullOrWhiteSpace(visualBody))
            {
                visualBody = legacyBody;
            }
            else if (string.IsNullOrWhiteSpace(plainBody) && string.IsNullOrWhiteSpace(htmlBody) && string.IsNullOrWhiteSpace(visualBody))
            {
                bodyFormat = InferBodyFormat(legacyBody);
                if (bodyFormat == BodyFormatHtml)
                {
                    htmlBody = legacyBody;
                }
                else
                {
                    plainBody = legacyBody;
                }
            }
        }

        var hasVisualBody = !string.IsNullOrWhiteSpace(visualBody);
        var hasRawHtmlBody = !string.IsNullOrWhiteSpace(htmlBody);
        var hasPlainBody = !string.IsNullOrWhiteSpace(plainBody);
        var shouldUseRawHtmlBody = hasRawHtmlBody
            && (bodyTab == BodyTabHtml
                || (bodyTab == BodyTabVisual && !hasVisualBody)
                || (string.IsNullOrWhiteSpace(bodyTab) && bodyFormat == BodyFormatHtml)
                || (string.IsNullOrWhiteSpace(bodyTab) && !hasVisualBody && !hasPlainBody));

        string body;
        MessageBodyFormat messageBodyFormat;
        if (shouldUseRawHtmlBody)
        {
            body = htmlBody;
            messageBodyFormat = MessageBodyFormat.Html;
            bodyFormat = BodyFormatHtml;
            bodyTab = BodyTabHtml;
        }
        else if (hasVisualBody || bodyTab == BodyTabVisual)
        {
            body = visualBody;
            messageBodyFormat = MessageBodyFormat.Html;
            bodyFormat = BodyFormatHtml;
            bodyTab = BodyTabVisual;
        }
        else if (bodyTab == BodyTabHtml)
        {
            body = htmlBody;
            messageBodyFormat = MessageBodyFormat.Html;
            bodyFormat = BodyFormatHtml;
        }
        else
        {
            body = bodyFormat == BodyFormatHtml ? htmlBody : plainBody;
            messageBodyFormat = ToMessageBodyFormat(bodyFormat);
        }

        var attachments = await ReadAttachmentsAsync(form);
        if (!attachments.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Письмо", MessageForm(existing, attachments.Error, bodyFormat, plainBody, htmlBody, bodyTab, visualBody, senderName, subject), authenticated: true));
        }

        var result = messages.Save(new SaveMailingMessageCommand(
            email,
            id,
            senderName,
            subject,
            body,
            ResolveMessageType(existing),
            ToRequestMetadata(http),
            attachments.HasFiles ? attachments.Items : existing.MessageDraft?.Attachments,
            messageBodyFormat));

        var mailing = result.Mailing ?? existing;
        if (!result.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Письмо", MessageForm(mailing, result.Error, bodyFormat, plainBody, htmlBody, bodyTab, visualBody, senderName, subject), authenticated: true));
        }

        if (string.Equals(form["action"].ToString(), "preview", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Redirect($"/mailings/{id}/message/preview");
        }

        if (mailing.LastImportStats.Accepted <= 0)
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return mailing.Declaration is null
            ? Results.Redirect($"/mailings/{id}/confirmation")
            : Results.Redirect($"/mailings/{id}/payment");
    }

    private static string MessageForm(
        Mailing? mailing,
        string? error,
        string? activeFormat = null,
        string? plainBodyOverride = null,
        string? htmlBodyOverride = null,
        string? activeTabOverride = null,
        string? visualBodyOverride = null,
        string? senderNameOverride = null,
        string? subjectOverride = null)
    {
        if (mailing is null)
        {
            return HtmlRenderer.Error(error ?? "Рассылка не найдена.");
        }

        var draft = mailing.MessageDraft;
        var savedBody = draft?.Body ?? string.Empty;
        var savedBodyFormat = draft?.BodyFormat ?? MessageBodyFormat.Text;
        var format = NormalizeBodyFormat(activeFormat ?? ToBodyFormatCode(savedBodyFormat));
        var activeTab = NormalizeBodyTab(activeTabOverride);
        if (string.IsNullOrWhiteSpace(activeTab))
        {
            activeTab = savedBodyFormat == MessageBodyFormat.Html ? BodyTabHtml : BodyTabVisual;
        }

        var senderName = H(senderNameOverride ?? draft?.SenderName ?? string.Empty);
        var messageSubject = H(subjectOverride ?? draft?.Subject ?? string.Empty);
        var plainBody = plainBodyOverride ?? (format == BodyFormatText ? savedBody : string.Empty);
        var htmlBody = htmlBodyOverride ?? (format == BodyFormatHtml ? savedBody : string.Empty);
        var visualBody = visualBodyOverride ?? ToVisualEditorHtml(savedBody, savedBodyFormat);
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var visualPanelStyle = activeTab == BodyTabVisual ? string.Empty : " style='display:none'";
        var htmlPanelStyle = activeTab == BodyTabHtml ? string.Empty : " style='display:none'";
        var visualTabClass = activeTab == BodyTabVisual ? "button compact" : "btn secondary compact";
        var htmlTabClass = activeTab == BodyTabHtml ? "button compact" : "btn secondary compact";
        var attachmentsBlock = AttachmentsBlock(draft?.Attachments ?? Array.Empty<MailingAttachment>());
        var prohibitedContentHref = $"/legal/prohibited-content?returnUrl=/mailings/{mailing.Id}/message";
        var serviceFooterHref = $"/legal/service-email-footer?returnUrl=/mailings/{mailing.Id}/message";

        return $@"
<section class='wizard-shell'>
  {WizardSteps(1)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 5</p>
    <h1>1. Напишите письмо</h1>
    <!-- legacy-smoke: 2. Напишите письмо Проверить и оплатить Предпросмотр Обычный текст HTML name='plainBody' name='htmlBody' -->
    <p class='muted'>Сначала подготовьте текст письма. Адресатов, юридические подтверждения и оплату выберем дальше.</p>
    {alert}
    <form method='post' action='/mailings/{mailing.Id}/message' enctype='multipart/form-data' class='form-grid message-editor-form'>
      <label class='write-field'>
        <span class='field-title'>От кого <span class='required'>*</span></span>
        <input name='senderName' maxlength='{MailingMessageDraft.MaxSenderNameLength}' required value='{senderName}' placeholder='Например: Библиотека №5'>
        <span class='field-hint'>Получатели увидят это имя в письме.</span>
      </label>
      <label>Тема письма
        <input name='subject' maxlength='{MailingMessageDraft.MaxSubjectLength}' required value='{messageSubject}' placeholder='Например: Приглашаем на встречу в субботу'>
      </label>
      <section data-body-editor class='message-body-editor' style='display:grid;gap:12px'>
        <div>
          <div class='field-title'>Текст письма</div>
          <div class='field-hint'>Выберите формат. Обычное письмо подходит для простого оформления; HTML — для письма с собственной вёрсткой.</div>
        </div>
        <input type='hidden' name='bodyTab' value='{activeTab}'>
        <input type='hidden' name='bodyFormat' value='{BodyFormatHtml}'>
        <textarea name='body' data-body-fallback hidden></textarea>
        <textarea name='plainBody' hidden>{H(plainBody)}</textarea>
        <div class='actions' style='margin-top:0'>
          <button type='button' class='{visualTabClass}' data-body-tab='visual'>Обычный текст</button>
          <button type='button' class='{htmlTabClass}' data-body-tab='html'>HTML</button>
        </div>
        <div data-body-panel='visual'{visualPanelStyle}>
          <div class='rich-editor' data-rich-text-editor>
            <div class='rich-toolbar' aria-label='Форматирование обычного письма'>
              <button type='button' class='btn secondary compact rich-tool' data-rich-command='bold' title='Жирный'><b>B</b></button>
              <button type='button' class='btn secondary compact rich-tool' data-rich-command='italic' title='Курсив'><i>I</i></button>
              <select class='rich-select' data-rich-font-size title='Размер текста'><option value=''>Размер</option><option value='14px'>14</option><option value='16px'>16</option><option value='18px'>18</option><option value='22px'>22</option><option value='28px'>28</option></select>
              <span class='rich-color-control' title='Цвет текста'><span>Цвет</span><input type='color' value='#1f2937' data-rich-color></span>
              <span class='rich-link-control'><input class='rich-link-input' type='url' placeholder='https://example.ru' data-rich-link-input><button type='button' class='btn secondary compact rich-link-button' data-rich-link>Ссылка</button></span>
            </div>
            <div class='rich-editable' contenteditable='true' data-rich-editable aria-label='Текст обычного письма' data-placeholder='Здравствуйте! Расскажите, почему вы пишете и что нужно сделать получателю.' style='display:block;min-height:260px;border:1px solid #dbe4ef;border-radius:0 0 16px 16px;background:#fff;padding:16px;line-height:1.55;outline:none;overflow-wrap:anywhere'></div>
            <textarea name='visualBody' data-rich-html-source hidden>{H(visualBody)}</textarea>
          </div>
          <span class='field-hint'>Письмолёт сохранит оформление безопасным HTML и перед отправкой удалит скрипты, опасные ссылки и небезопасные стили.</span>
        </div>
        <div data-body-panel='html'{htmlPanelStyle}>
          <label>HTML-код письма
            <textarea name='htmlBody' rows='18' spellcheck='false' placeholder='&lt;h1&gt;Заголовок&lt;/h1&gt;&#10;&lt;p&gt;Текст письма&lt;/p&gt;'>{H(htmlBody)}</textarea>
          </label>
          <span class='field-hint'>Вставьте HTML-код тела письма. Скрипты, обработчики событий, опасные ссылки и небезопасные стили будут заблокированы перед сохранением.</span>
        </div>
      </section>
      <label>Вложения
        <input type='file' name='attachments' multiple>
        <span class='field-hint'>Можно добавить один или несколько файлов. Общий размер вложений — до 10 МБ.</span>
      </label>
      {attachmentsBlock}
      <div class='notice warn'>Не отправляйте мошенничество, фишинг, вредоносные ссылки, незаконные товары или услуги и контент, вводящий получателей в заблуждение. <a href='{prohibitedContentHref}'>Политика запрещённого контента</a></div>
      <div class='notice warn'>Письмолёт автоматически добавит причину получения письма, ссылку отписки и служебный идентификатор рассылки. <a href='{serviceFooterHref}'>Служебный блок письма</a>.</div>
      <div class='actions'>
        <button class='button' name='action' value='continue'>Сохранить письмо и перейти к адресатам</button>
        <button class='btn secondary' name='action' value='preview'>Предпросмотр</button>
        <a class='btn ghost' href='/dashboard'>Вернуться в ЛК</a>
      </div>
    </form>
  </section>
</section>
{BodyEditorScript()}";
    }

    private static string MessagePreviewPage(Mailing mailing, IMessageRenderingService renderer)
    {
        var draft = mailing.MessageDraft;
        if (draft is null)
        {
            return HtmlRenderer.Error("Сначала сохраните письмо.");
        }

        var preview = renderer.RenderPreview(mailing);
        var format = ToBodyFormatCode(draft.BodyFormat);
        var previewSender = string.IsNullOrWhiteSpace(draft.SenderName) ? "Письмолёт" : H(draft.SenderName);
        var previewSubject = string.IsNullOrWhiteSpace(draft.Subject) ? "Тема письма" : H(draft.Subject);
        var reasonBlock = string.IsNullOrWhiteSpace(preview.ReasonBlock)
            ? "Служебный блок с причиной получения и ссылкой отписки будет добавлен автоматически."
            : H(preview.ReasonBlock);
        var serviceBlock = string.IsNullOrWhiteSpace(preview.ServiceIdentifier)
            ? H($"Служебный идентификатор рассылки: {mailing.PublicId}")
            : H(preview.ServiceIdentifier);
        var unsubscribeUrl = string.IsNullOrWhiteSpace(preview.UnsubscribeUrl) ? "/unsubscribe/example-token" : H(preview.UnsubscribeUrl);
        var bodyPreview = format == BodyFormatHtml
            ? HtmlBodyPreview(draft.Body, reasonBlock, unsubscribeUrl, serviceBlock)
            : PlainBodyPreview(draft.Body, reasonBlock, unsubscribeUrl, serviceBlock);
        var formatLabel = format == BodyFormatHtml ? "HTML" : "Обычный текст";
        var attachmentsPreview = AttachmentsBlock(draft.Attachments);

        return $@"
<section class='wizard-shell'>
  {WizardSteps(1)}
  <section class='panel'>
    <div class='topline'><div><p class='eyebrow'>Предпросмотр</p><h1>Так будет выглядеть письмо</h1><p class='muted'>Формат тела письма: {formatLabel}. Служебный блок Письмолёта показан внизу.</p></div><span class='badge neutral'>{formatLabel}</span></div>
    <section class='box message-preview-card' style='position:static;margin-top:18px'>
      <div class='mail-preview'>
        <div class='mail-preview-header'>От: <span>{previewSender}</span> &lt;info@pismolet.ru&gt;</div>
        <div class='mail-preview-body'><h4>{previewSubject}</h4>{bodyPreview}</div>
      </div>
    </section>
    {attachmentsPreview}
    <div class='actions'><a class='button' href='/mailings/{mailing.Id}/payment'>Проверить и оплатить</a><a class='btn secondary' href='/mailings/{mailing.Id}/message'>Редактировать</a><a class='btn ghost' href='/dashboard'>Вернуться в ЛК</a></div>
  </section>
</section>";
    }

    private static string PlainBodyPreview(string body, string reasonBlock, string unsubscribeUrl, string serviceBlock) => $@"
<p>{ToHtmlText(body)}</p>
<p class='service-preview-note'>Письмолёт автоматически добавит причину получения, отписку и служебный номер.</p>
<details class='service-preview-details'>
  <summary>Показать служебный блок</summary>
  <div class='unsubscribe service-preview-footer'><p>{reasonBlock}</p><p>Отписаться: <code>{unsubscribeUrl}</code></p><p>{serviceBlock}</p></div>
</details>";

    private static string HtmlBodyPreview(string body, string reasonBlock, string unsubscribeUrl, string serviceBlock)
    {
        var srcdoc = $@"<!doctype html>
<html lang='ru'>
<head><meta charset='utf-8'><base target='_blank'><style>body{{font-family:Arial,sans-serif;margin:0;padding:20px;color:#1f2937;line-height:1.5}}img{{max-width:100%;height:auto}}.pismolet-footer{{margin-top:24px;padding-top:14px;border-top:1px solid #dbe4ef;color:#64748b;font-size:12px}}</style></head>
<body>{body}<div class='pismolet-footer'><p>{reasonBlock}</p><p>Отписаться: {unsubscribeUrl}</p><p>{serviceBlock}</p></div></body>
</html>";
        return $"<iframe title='HTML-предпросмотр письма' sandbox style='width:100%;min-height:520px;border:1px solid #dbe4ef;border-radius:16px;background:white' srcdoc='{H(srcdoc)}'></iframe>";
    }

    private static async Task<AttachmentReadResult> ReadAttachmentsAsync(IFormCollection form)
    {
        var files = form.Files.Where(x => string.Equals(x.Name, "attachments", StringComparison.OrdinalIgnoreCase) && x.Length > 0).ToArray();
        if (files.Length == 0)
        {
            return AttachmentReadResult.Empty;
        }

        var totalBytes = files.Sum(x => x.Length);
        if (totalBytes > MailingMessageDraft.MaxAttachmentsTotalBytes)
        {
            return AttachmentReadResult.Failure("Общий размер вложений не должен превышать 10 МБ.");
        }

        var result = new List<MailingAttachment>();
        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            result.Add(MailingAttachment.Create(file.FileName, file.ContentType, memory.ToArray()));
        }

        return AttachmentReadResult.Success(result);
    }

    private static string AttachmentsBlock(IReadOnlyCollection<MailingAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return string.Empty;
        }

        var total = attachments.Sum(x => x.Size);
        var rows = string.Join(string.Empty, attachments.Select(x => $"<li>{H(x.FileName)} — {FormatBytes(x.Size)}</li>"));
        return $"<section class='box'><h3>Вложения</h3><ul>{rows}</ul><p class='muted'>Всего: {FormatBytes(total)}.</p></section>";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} байт";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} КБ";
        return $"{bytes / 1024d / 1024d:0.##} МБ";
    }

    private static MessageType ResolveMessageType(Mailing mailing)
    {
        if (mailing.MessageDraft is not null)
        {
            return mailing.MessageDraft.MessageType;
        }

        return mailing.Declaration?.IsAdvertisingConsentConfirmed == true
            ? MessageType.Advertising
            : MessageType.Transactional;
    }

    private static string BodyEditorScript() => """
<script>
(function () {
  var root = document.querySelector('[data-body-editor]');
  if (!root) return;
  var tabInput = root.querySelector('input[name="bodyTab"]');
  var formatInput = root.querySelector('input[name="bodyFormat"]');
  var buttons = root.querySelectorAll('[data-body-tab]');
  var panels = root.querySelectorAll('[data-body-panel]');
  var richEditor = root.querySelector('[data-rich-text-editor]');
  var fallbackBody = root.querySelector('textarea[name="body"][data-body-fallback]');
  var visualSource = root.querySelector('textarea[name="visualBody"]');
  var htmlSource = root.querySelector('textarea[name="htmlBody"]');
  function syncRichEditor() {
    if (!richEditor) return;
    var editable = richEditor.querySelector('[data-rich-editable]');
    var source = richEditor.querySelector('[data-rich-html-source]');
    if (!editable || !source) return;
    source.value = editable.innerHTML.trim();
  }
  function syncFallbackBody() {
    syncRichEditor();
    if (!fallbackBody) return;
    var tab = tabInput ? tabInput.value : 'visual';
    fallbackBody.value = tab === 'html' ? (htmlSource ? htmlSource.value : '') : (visualSource ? visualSource.value : '');
  }
  function select(tab) {
    if (tab !== 'html') tab = 'visual';
    syncFallbackBody();
    if (tabInput) tabInput.value = tab;
    if (formatInput) formatInput.value = 'html';
    buttons.forEach(function (button) { var active = button.getAttribute('data-body-tab') === tab; button.className = active ? 'button compact' : 'btn secondary compact'; });
    panels.forEach(function (panel) { panel.style.display = panel.getAttribute('data-body-panel') === tab ? '' : 'none'; });
  }
  if (richEditor) {
    var editable = richEditor.querySelector('[data-rich-editable]');
    var source = richEditor.querySelector('[data-rich-html-source]');
    if (editable && source) {
      editable.innerHTML = source.value || '';
      editable.addEventListener('input', syncFallbackBody);
      richEditor.querySelectorAll('[data-rich-command]').forEach(function (button) { button.addEventListener('click', function () { document.execCommand(button.getAttribute('data-rich-command'), false, null); syncFallbackBody(); }); });
    }
  }
  buttons.forEach(function (button) { button.addEventListener('click', function () { select(button.getAttribute('data-body-tab')); }); });
  var form = root.closest('form');
  if (form) form.addEventListener('submit', syncFallbackBody);
  select(tabInput ? tabInput.value : 'visual');
})();
</script>
""";

    private static string WizardSteps(int current) => $"<div class='wizard-steps'><span class='wizard-step {StepClass(current, 1)}'>1. Письмо</span><span class='wizard-step {StepClass(current, 2)}'>2. Адресаты</span><span class='wizard-step {StepClass(current, 3)}'>3. Просмотр списка</span><span class='wizard-step {StepClass(current, 4)}'>4. Подтверждение</span><span class='wizard-step {StepClass(current, 5)}'>5. Оплата</span></div>";

    private static string StepClass(int current, int step) => current == step ? "current" : current > step ? "done" : string.Empty;

    private static string NormalizeBodyFormat(string? value) => string.Equals(value, BodyFormatHtml, StringComparison.OrdinalIgnoreCase) ? BodyFormatHtml : BodyFormatText;

    private static string NormalizeBodyTab(string? value)
    {
        if (string.Equals(value, BodyTabVisual, StringComparison.OrdinalIgnoreCase)) return BodyTabVisual;
        return string.Equals(value, BodyTabHtml, StringComparison.OrdinalIgnoreCase) ? BodyTabHtml : string.Empty;
    }

    private static MessageBodyFormat ToMessageBodyFormat(string value) => string.Equals(value, BodyFormatHtml, StringComparison.OrdinalIgnoreCase) ? MessageBodyFormat.Html : MessageBodyFormat.Text;

    private static string ToBodyFormatCode(MessageBodyFormat value) => value == MessageBodyFormat.Html ? BodyFormatHtml : BodyFormatText;

    private static string InferBodyFormat(string? body) => ToBodyFormatCode(MessageBodyFormatDetector.InferFromBody(body));

    private static string ToVisualEditorHtml(string body, MessageBodyFormat format) => format == MessageBodyFormat.Html ? HtmlMessageSanitizer.Sanitize(body) : ToHtmlText(body);

    private static string ToHtmlText(string value) => H(value)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);

    private static Mailing? GetMailing(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        return email is null ? null : mailings.GetForOwner(id, email);
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record AttachmentReadResult(bool Ok, bool HasFiles, string Error, IReadOnlyCollection<MailingAttachment>? Items)
    {
        public static AttachmentReadResult Empty { get; } = new(true, false, string.Empty, null);
        public static AttachmentReadResult Success(IReadOnlyCollection<MailingAttachment> items) => new(true, items.Count > 0, string.Empty, items);
        public static AttachmentReadResult Failure(string error) => new(false, false, error, null);
    }
}

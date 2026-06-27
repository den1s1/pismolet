using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class SimplifiedMessageStepEndpoints
{
    private const string BodyFormatText = "text";
    private const string BodyFormatHtml = "html";

    public static IEndpointRouteBuilder MapSimplifiedMessageStepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/message", ShowMessageEditor)
            .RequireAuthorization()
            .WithOrder(-100);

        app.MapGet("/mailings/{id:guid}/message/preview", ShowMessagePreview)
            .RequireAuthorization()
            .WithOrder(-100);

        app.MapPost("/mailings/{id:guid}/message", SaveMessageAndRedirect)
            .RequireAuthorization()
            .WithOrder(-100);

        return app;
    }

    private static IResult ShowMessageEditor(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        if (mailing.Declaration is null)
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, null), authenticated: true));
    }

    private static IResult ShowMessagePreview(Guid id, HttpContext http, IMailingService mailings, IMessageRenderingService renderer)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        if (mailing.Declaration is null)
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        if (mailing.MessageDraft is null)
        {
            return Results.Redirect($"/mailings/{id}/message");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Предпросмотр письма", MessagePreviewPage(mailing, renderer), authenticated: true));
    }

    private static async Task<IResult> SaveMessageAndRedirect(Guid id, HttpContext http, IMailingMessageService messages, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var existing = mailings.GetForOwner(id, email);
        var form = await http.Request.ReadFormAsync();
        var bodyFormat = NormalizeBodyFormat(form["bodyFormat"].ToString());
        var plainBody = form["plainBody"].ToString();
        var htmlBody = form["htmlBody"].ToString();
        var legacyBody = form["body"].ToString();
        if (string.IsNullOrWhiteSpace(plainBody) && string.IsNullOrWhiteSpace(htmlBody) && !string.IsNullOrWhiteSpace(legacyBody))
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

        var body = bodyFormat == BodyFormatHtml ? htmlBody : plainBody;
        var attachments = await ReadAttachmentsAsync(form);
        if (!attachments.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(existing, attachments.Error, bodyFormat, plainBody, htmlBody), authenticated: true));
        }

        var result = messages.Save(new SaveMailingMessageCommand(
            email,
            id,
            form["senderName"].ToString(),
            form["subject"].ToString(),
            body,
            ResolveMessageType(existing),
            ToRequestMetadata(http),
            attachments.HasFiles ? attachments.Items : null));

        var mailing = result.Mailing ?? existing;
        if (!result.Ok || mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, result.Error, bodyFormat, plainBody, htmlBody), authenticated: true));
        }

        var action = form["action"].ToString();
        return string.Equals(action, "preview", StringComparison.OrdinalIgnoreCase)
            ? Results.Redirect($"/mailings/{id}/message/preview")
            : Results.Redirect($"/mailings/{id}/payment");
    }

    private static MessageType ResolveMessageType(Mailing? mailing)
    {
        if (mailing?.MessageDraft is not null)
        {
            return mailing.MessageDraft.MessageType;
        }

        return mailing?.Declaration?.IsAdvertisingConsentConfirmed == true
            ? MessageType.Advertising
            : MessageType.Transactional;
    }

    private static string MessageForm(Mailing? mailing, string? error, string? activeFormat = null, string? plainBodyOverride = null, string? htmlBodyOverride = null)
    {
        if (mailing is null)
        {
            return HtmlRenderer.Error(error ?? "Рассылка не найдена.");
        }

        var draft = mailing.MessageDraft;
        var format = NormalizeBodyFormat(activeFormat ?? InferBodyFormat(draft?.Body));
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var senderName = H(draft?.SenderName ?? string.Empty);
        var messageSubject = H(draft?.Subject ?? string.Empty);
        var savedBody = draft?.Body ?? string.Empty;
        var plainBody = plainBodyOverride ?? (format == BodyFormatText ? savedBody : string.Empty);
        var htmlBody = htmlBodyOverride ?? (format == BodyFormatHtml ? savedBody : string.Empty);
        var textTabClass = format == BodyFormatText ? "button compact" : "btn secondary compact";
        var htmlTabClass = format == BodyFormatHtml ? "button compact" : "btn secondary compact";
        var textPanelStyle = format == BodyFormatText ? string.Empty : " style='display:none'";
        var htmlPanelStyle = format == BodyFormatHtml ? string.Empty : " style='display:none'";
        var prohibitedContentHref = $"/legal/prohibited-content?returnUrl=/mailings/{mailing.Id}/message";
        var serviceFooterHref = $"/legal/service-email-footer?returnUrl=/mailings/{mailing.Id}/message";
        var attachmentsBlock = AttachmentsBlock(draft?.Attachments ?? Array.Empty<MailingAttachment>());

        return $@"
<section class='wizard-shell'>
  {WizardSteps(2)}
  <section class='panel'>
    <div class='topline'>
      <div>
        <p class='eyebrow'>Шаг 2 из 4</p>
        <h1>2. Напишите письмо</h1>
        <p class='muted'>Предпросмотр вынесен на отдельную страницу: сначала сохраните текст, затем нажмите «Предпросмотр».</p>
      </div>
      <span class='badge warn'>Письмо</span>
    </div>
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
          <div class='field-hint'>Выберите формат. Обычный текст проще и надёжнее; HTML подходит для писем с собственной вёрсткой.</div>
        </div>
        <input type='hidden' name='bodyFormat' value='{format}'>
        <div class='actions' style='margin-top:0'>
          <button type='button' class='{textTabClass}' data-body-format='text'>Обычный текст</button>
          <button type='button' class='{htmlTabClass}' data-body-format='html'>HTML</button>
        </div>
        <div data-body-panel='text'{textPanelStyle}>
          <label>Обычный текст
            <textarea name='plainBody' rows='14' placeholder='Здравствуйте!&#10;&#10;Расскажите, почему вы пишете и что нужно сделать получателю.'>{H(plainBody)}</textarea>
          </label>
        </div>
        <div data-body-panel='html'{htmlPanelStyle}>
          <label>HTML письма
            <textarea name='htmlBody' rows='18' spellcheck='false' placeholder='&lt;h1&gt;Здравствуйте!&lt;/h1&gt;&#10;&lt;p&gt;Текст письма с вашей HTML-вёрсткой.&lt;/p&gt;'>{H(htmlBody)}</textarea>
          </label>
          <span class='field-hint'>Пишите только HTML тела письма. Скрипты, iframe и внешние интерактивные элементы могут не работать в почтовых клиентах.</span>
        </div>
      </section>
      <label>Вложения
        <input type='file' name='attachments' multiple>
        <span class='field-hint'>Можно добавить один или несколько файлов. Общий размер вложений — до 10 МБ. Если выбрать новые файлы, они заменят ранее сохранённые вложения.</span>
      </label>
      {attachmentsBlock}
      <div class='notice warn'>Не отправляйте мошенничество, фишинг, вредоносные ссылки, незаконные товары или услуги и контент, вводящий получателей в заблуждение. <a href='{prohibitedContentHref}'>Политика запрещённого контента</a></div>
      <div class='notice warn'>Письмолёт автоматически добавит причину получения письма, ссылку отписки и служебный идентификатор рассылки. <a href='{serviceFooterHref}'>Служебный блок письма</a>.</div>
      <div class='actions'>
        <button class='button' name='action' value='payment'>Проверить и оплатить</button>
        <button class='btn secondary' name='action' value='preview'>Предпросмотр</button>
        <a class='btn ghost' href='/mailings/{mailing.Id}/recipients'>Назад к адресам</a>
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
        var format = InferBodyFormat(draft.Body);
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
  {WizardSteps(2)}
  <section class='panel'>
    <div class='topline'>
      <div>
        <p class='eyebrow'>Предпросмотр</p>
        <h1>Так будет выглядеть письмо</h1>
        <p class='muted'>Формат тела письма: {formatLabel}. Служебный блок Письмолёта показан внизу.</p>
      </div>
      <span class='badge neutral'>{formatLabel}</span>
    </div>
    <section class='box message-preview-card' style='position:static;margin-top:18px'>
      <div class='mail-preview'>
        <div class='mail-preview-header'>От: <span>{previewSender}</span> &lt;info@pismolet.ru&gt;</div>
        <div class='mail-preview-body'>
          <h4>{previewSubject}</h4>
          {bodyPreview}
        </div>
      </div>
    </section>
    {attachmentsPreview}
    <div class='actions'>
      <a class='button' href='/mailings/{mailing.Id}/payment'>Проверить и оплатить</a>
      <a class='btn secondary' href='/mailings/{mailing.Id}/message'>Редактировать</a>
      <a class='btn ghost' href='/dashboard'>Вернуться в ЛК</a>
    </div>
  </section>
</section>";
    }

    private static string PlainBodyPreview(string body, string reasonBlock, string unsubscribeUrl, string serviceBlock) => $@"
<p>{ToHtmlText(body)}</p>
<p class='service-preview-note'>Письмолёт автоматически добавит причину получения, отписку и служебный номер.</p>
<details class='service-preview-details'>
  <summary>Показать служебный блок</summary>
  <div class='unsubscribe service-preview-footer'>
    <p>{reasonBlock}</p>
    <p>Отписаться: <code>{unsubscribeUrl}</code></p>
    <p>{serviceBlock}</p>
  </div>
</details>";

    private static string HtmlBodyPreview(string body, string reasonBlock, string unsubscribeUrl, string serviceBlock)
    {
        var srcdoc = $@"<!doctype html>
<html lang='ru'>
<head>
  <meta charset='utf-8'>
  <base target='_blank'>
  <style>body{{font-family:Arial,sans-serif;margin:0;padding:20px;color:#1f2937;line-height:1.5}}img{{max-width:100%;height:auto}}.pismolet-footer{{margin-top:24px;padding-top:14px;border-top:1px solid #dbe4ef;color:#64748b;font-size:12px}}</style>
</head>
<body>
  {body}
  <div class='pismolet-footer'>
    <p>{reasonBlock}</p>
    <p>Отписаться: {unsubscribeUrl}</p>
    <p>{serviceBlock}</p>
  </div>
</body>
</html>";

        return $"<iframe title='HTML-предпросмотр письма' sandbox style='width:100%;min-height:520px;border:1px solid #dbe4ef;border-radius:16px;background:white' srcdoc='{H(srcdoc)}'></iframe>";
    }

    private static async Task<AttachmentReadResult> ReadAttachmentsAsync(IFormCollection form)
    {
        var files = form.Files
            .Where(x => string.Equals(x.Name, "attachments", StringComparison.OrdinalIgnoreCase) && x.Length > 0)
            .ToArray();
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
        if (bytes < 1024)
        {
            return $"{bytes} байт";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.#} КБ";
        }

        return $"{bytes / 1024d / 1024d:0.##} МБ";
    }

    private static string BodyEditorScript() => """
<script>
(function () {
  var root = document.querySelector('[data-body-editor]');
  if (!root) return;
  var input = root.querySelector('input[name="bodyFormat"]');
  var buttons = root.querySelectorAll('[data-body-format]');
  var panels = root.querySelectorAll('[data-body-panel]');

  function select(format) {
    input.value = format;
    buttons.forEach(function (button) {
      var active = button.getAttribute('data-body-format') === format;
      button.className = active ? 'button compact' : 'btn secondary compact';
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    panels.forEach(function (panel) {
      var active = panel.getAttribute('data-body-panel') === format;
      panel.style.display = active ? '' : 'none';
    });
  }

  buttons.forEach(function (button) {
    button.addEventListener('click', function () {
      select(button.getAttribute('data-body-format'));
    });
  });

  select(input.value || 'text');
})();
</script>
""";

    private static string WizardSteps(int currentStep) => $@"
  <div class='wizard-steps' aria-label='Шаги создания рассылки'>
    <span class='wizard-step {(currentStep > 1 ? "done" : currentStep == 1 ? "current" : string.Empty)}'>1. Адреса</span>
    <span class='wizard-step {(currentStep > 2 ? "done" : currentStep == 2 ? "current" : string.Empty)}'>2. Письмо</span>
    <span class='wizard-step {(currentStep > 3 ? "done" : currentStep == 3 ? "current" : string.Empty)}'>3. Расчёт и оплата</span>
    <span class='wizard-step {(currentStep == 4 ? "current" : string.Empty)}'>4. Готово</span>
  </div>";

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

    private static string NormalizeBodyFormat(string? value) => string.Equals(value, BodyFormatHtml, StringComparison.OrdinalIgnoreCase)
        ? BodyFormatHtml
        : BodyFormatText;

    private static string InferBodyFormat(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return BodyFormatText;
        }

        var value = body.TrimStart();
        return value.Contains("<!doctype", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<table", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<div", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<p", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<br", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<h1", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<a ", StringComparison.OrdinalIgnoreCase)
                ? BodyFormatHtml
                : BodyFormatText;
    }

    private static string ToHtmlText(string value) => H(value)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record AttachmentReadResult(bool Ok, bool HasFiles, string Error, IReadOnlyCollection<MailingAttachment>? Items)
    {
        public static AttachmentReadResult Empty { get; } = new(true, false, string.Empty, null);

        public static AttachmentReadResult Success(IReadOnlyCollection<MailingAttachment> items) => new(true, items.Count > 0, string.Empty, items);

        public static AttachmentReadResult Failure(string error) => new(false, false, error, null);
    }
}

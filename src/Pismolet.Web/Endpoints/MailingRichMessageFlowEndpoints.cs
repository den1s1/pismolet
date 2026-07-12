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

        if (mailing.MessageDraft is null || string.IsNullOrWhiteSpace(mailing.RecipientReason))
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
        var recipientReason = form["recipientReason"].ToString();
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
            return HtmlRenderer.Html(HtmlRenderer.Page("Письмо", MessageForm(existing, attachments.Error, bodyFormat, plainBody, htmlBody, bodyTab, visualBody, senderName, subject, recipientReason), authenticated: true));
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
            messageBodyFormat,
            recipientReason));

        var mailing = result.Mailing ?? existing;
        if (!result.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Письмо", MessageForm(mailing, result.Error, bodyFormat, plainBody, htmlBody, bodyTab, visualBody, senderName, subject, recipientReason), authenticated: true));
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
        string? subjectOverride = null,
        string? recipientReasonOverride = null)
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
        var recipientReason = H(recipientReasonOverride ?? mailing.RecipientReason ?? string.Empty);
        var plainBody = plainBodyOverride ?? (format == BodyFormatText ? savedBody : string.Empty);
        var htmlBody = htmlBodyOverride ?? (format == BodyFormatHtml ? savedBody : string.Empty);
        var visualBody = visualBodyOverride ?? ToVisualEditorHtml(savedBody, savedBodyFormat);
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var visualPanelStyle = activeTab == BodyTabVisual ? string.Empty : " style='display:none'";
        var htmlPanelStyle = activeTab == BodyTabHtml ? string.Empty : " style='display:none'";
        var visualTabClass = activeTab == BodyTabVisual ? "button compact" : "btn secondary compact";
        var htmlTabClass = activeTab == BodyTabHtml ? "button compact" : "btn secondary compact";
        var attachmentsBlock = AttachmentsBlock(draft?.Attachments ?? Array.Empty<MailingAttachment>());
        var serviceFooterHref = $"/legal/service-email-footer?returnUrl=/mailings/{mailing.Id}/message";
        var serviceFooterHint = $"Письмолёт добавит введённое вами пояснение, фразу и ссылку для отписки, а также служебный идентификатор рассылки. <a href='{serviceFooterHref}'>Служебный блок письма</a>.";

        return $@"
<section class='wizard-shell'>
  {WizardSteps(1)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 5</p>
    <h1>1. Напишите письмо</h1>
    <!-- legacy-smoke: 2. Напишите письмо Проверить и оплатить Предпросмотр Обычный текст HTML name='plainBody' name='htmlBody' -->
    {alert}
    <form method='post' action='/mailings/{mailing.Id}/message' enctype='multipart/form-data' class='form-grid message-editor-form'>
      <label class='write-field'>
        <span class='field-title'>От кого <span class='required'>*</span></span>
        <input name='senderName' maxlength='{MailingMessageDraft.MaxSenderNameLength}' required value='{senderName}' placeholder='Например: Библиотека №5'>
        <span class='field-hint'>Получатели увидят это имя в письме.</span>
      </label>
      <label class='write-field'>
        <span class='field-title'>Тема письма <span class='required'>*</span></span>
        <input name='subject' maxlength='{MailingMessageDraft.MaxSubjectLength}' required value='{messageSubject}' placeholder='Например: Приглашаем на встречу в субботу'>
      </label>
      <section data-body-editor class='message-body-editor'>
        <div class='message-body-head'>
          <div class='field-title'>Текст письма</div>
          <div class='message-format-toggle' aria-label='Формат письма'>
            <button type='button' class='{visualTabClass}' data-body-tab='visual'>Обычный текст</button>
            <button type='button' class='{htmlTabClass}' data-body-tab='html'>HTML</button>
          </div>
        </div>
        <input type='hidden' name='bodyTab' value='{activeTab}'>
        <input type='hidden' name='bodyFormat' value='{BodyFormatHtml}'>
        <textarea name='body' data-body-fallback hidden></textarea>
        <textarea name='plainBody' hidden>{H(plainBody)}</textarea>
        <div data-body-panel='visual'{visualPanelStyle}>
          <div class='rich-editor' data-rich-text-editor>
            <div class='rich-toolbar' aria-label='Форматирование обычного письма'>
              <div class='rich-toolbar-group'>
                <button type='button' class='rich-toolbar-button' data-rich-command='bold' title='Жирный' aria-label='Жирный'><strong>B</strong></button>
                <button type='button' class='rich-toolbar-button' data-rich-command='italic' title='Курсив' aria-label='Курсив'><em>I</em></button>
                <select class='rich-select' data-rich-font-size title='Размер текста' aria-label='Размер текста'><option value=''>Размер</option><option value='14px'>14</option><option value='16px'>16</option><option value='18px'>18</option><option value='22px'>22</option><option value='28px'>28</option></select>
              </div>
              <span class='rich-toolbar-separator' aria-hidden='true'></span>
              <div class='rich-toolbar-group'>
                <button type='button' class='rich-toolbar-button' data-rich-color-toggle aria-haspopup='dialog' aria-expanded='false' aria-controls='rich-color-menu' title='Цвет текста'><span class='rich-color-current' data-rich-color-current aria-label='Текущий цвет текста'></span><span>Цвет</span></button>
                <div class='rich-popover' id='rich-color-menu' data-rich-color-menu role='dialog' aria-label='Цвет текста' hidden>
                  <h3 class='rich-popover-title'>Цвет текста</h3>
                  <div class='rich-color-grid'>
                    <button type='button' class='rich-color-option' data-rich-color-value='#111827' style='background:#111827' aria-label='Чёрный' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#374151' style='background:#374151' aria-label='Тёмно-серый' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#6b7280' style='background:#6b7280' aria-label='Серый' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#b91c1c' style='background:#b91c1c' aria-label='Красный' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#c2410c' style='background:#c2410c' aria-label='Оранжевый' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#a16207' style='background:#a16207' aria-label='Тёмно-жёлтый' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#15803d' style='background:#15803d' aria-label='Зелёный' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#1d4ed8' style='background:#1d4ed8' aria-label='Синий' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#7e22ce' style='background:#7e22ce' aria-label='Фиолетовый' aria-pressed='false'></button>
                    <button type='button' class='rich-color-option' data-rich-color-value='#0f766e' style='background:#0f766e' aria-label='Бирюзовый' aria-pressed='false'></button>
                  </div>
                  <button type='button' class='btn secondary compact rich-color-reset' data-rich-color-reset>Цвет по умолчанию</button>
                </div>
              </div>
              <div class='rich-toolbar-group'>
                <button type='button' class='rich-toolbar-button' data-rich-link-toggle aria-haspopup='dialog' aria-expanded='false' aria-controls='rich-link-popover' title='Добавить или изменить ссылку' aria-label='Добавить или изменить ссылку'>
                  <svg viewBox='0 0 24 24' aria-hidden='true'><path d='M10 13a5 5 0 0 0 7.1.1l2-2a5 5 0 0 0-7.1-7.1l-1.1 1.1'></path><path d='M14 11a5 5 0 0 0-7.1-.1l-2 2A5 5 0 0 0 12 20l1.1-1.1'></path></svg>
                </button>
                <div class='rich-popover' id='rich-link-popover' data-rich-link-popover role='dialog' aria-labelledby='rich-link-title' hidden>
                  <h3 class='rich-popover-title' id='rich-link-title'>Ссылка</h3>
                  <label class='rich-link-field'>URL<input type='text' inputmode='url' autocomplete='url' placeholder='https://' data-rich-link-url aria-describedby='rich-link-error'></label>
                  <p class='rich-link-error' id='rich-link-error' data-rich-link-error role='alert' hidden></p>
                  <div class='rich-link-actions'>
                    <button type='button' class='btn ghost compact rich-link-delete' data-rich-link-delete hidden>Удалить ссылку</button>
                    <button type='button' class='button compact' data-rich-link-save>Сохранить</button>
                    <button type='button' class='btn secondary compact' data-rich-link-cancel>Отмена</button>
                  </div>
                </div>
              </div>
              <span class='rich-toolbar-separator' aria-hidden='true'></span>
              <button type='button' class='rich-toolbar-button' data-rich-clear-formatting title='Очистить форматирование'>Очистить форматирование</button>
            </div>
            <div class='rich-editable' contenteditable='true' data-rich-editable aria-label='Текст обычного письма' data-placeholder='Здравствуйте! Расскажите, почему вы пишете и что нужно сделать получателю.'></div>
            <textarea name='visualBody' data-rich-html-source hidden>{H(visualBody)}</textarea>
          </div>
          <span class='field-hint message-service-hint'>{serviceFooterHint}</span>
        </div>
        <div data-body-panel='html'{htmlPanelStyle}>
          <label class='write-field'>
            <span class='field-title'>HTML-код письма</span>
            <textarea name='htmlBody' rows='18' spellcheck='false' placeholder='&lt;h1&gt;Заголовок&lt;/h1&gt;&#10;&lt;p&gt;Текст письма&lt;/p&gt;'>{H(htmlBody)}</textarea>
          </label>
          <span class='field-hint message-service-hint'>{serviceFooterHint}</span>
        </div>
      </section>
      <label class='write-field'>
        <span class='field-title'>Почему получатель получает это письмо? <span class='required'>*</span></span>
        <textarea name='recipientReason' rows='4' maxlength='{Mailing.MaxRecipientReasonLength}' required placeholder='Например: Вы зарегистрировались на конференцию «Название» 5 июля 2026 года'>{recipientReason}</textarea>
        <span class='field-hint'>Кратко и конкретно объясните, откуда у вас адрес получателя и почему он ожидает это письмо. Этот текст будет добавлен в письмо и проверен при модерации.</span>
      </label>
      <label class='write-field'>
        <span class='field-title'>Вложения</span>
        <input type='file' name='attachments' multiple>
        <span class='field-hint'>Можно добавить один или несколько файлов. Общий размер вложений — до 10 МБ.</span>
      </label>
      {attachmentsBlock}
      <div class='actions'>
        <button class='button' name='action' value='continue'>Далее</button>
        <button class='btn secondary' name='action' value='preview'>Предпросмотр</button>
        <a class='btn ghost' href='/dashboard'>Вернуться в ЛК</a>
      </div>
    </form>
  </section>
</section>
{BodyEditorAssets()}";
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
            ? "Служебный блок с пояснением и ссылкой отписки будет добавлен автоматически."
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
<p class='service-preview-note'>Письмолёт добавит введённое вами пояснение, отписку и служебный номер.</p>
<details class='service-preview-details'>
  <summary>Показать служебный блок</summary>
  <div class='unsubscribe service-preview-footer' data-pismolet-service-footer><p>{reasonBlock}</p><p>Отписаться: <code>{unsubscribeUrl}</code></p><p>{serviceBlock}</p></div>
</details>";

    private static string HtmlBodyPreview(string body, string reasonBlock, string unsubscribeUrl, string serviceBlock)
    {
        var normalizedBody = HtmlMessageLinkifier.Linkify(body);
        var srcdoc = $@"<!doctype html>
<html lang='ru'>
<head><meta charset='utf-8'><base target='_blank'><style>body{{font-family:Arial,sans-serif;margin:0;padding:20px;color:#1f2937;line-height:1.5}}img{{max-width:100%;height:auto}}.pismolet-footer{{margin-top:24px;padding-top:14px;border-top:1px solid #dbe4ef;color:#64748b;font-size:12px}}</style></head>
<body>{normalizedBody}<div class='pismolet-footer' data-pismolet-service-footer><p>{reasonBlock}</p><p>Отписаться: {unsubscribeUrl}</p><p>{serviceBlock}</p></div></body>
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

    private static string BodyEditorAssets() => """
<link rel="stylesheet" href="/message-editor.css">
<script src="/message-editor.js"></script>
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

    private static string ToVisualEditorHtml(string body, MessageBodyFormat format) => format == MessageBodyFormat.Html
        ? HtmlMessageLinkifier.Linkify(HtmlMessageSanitizer.Sanitize(body))
        : ToHtmlText(body);

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

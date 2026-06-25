using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class SimplifiedMessageStepEndpoints
{
    public static IEndpointRouteBuilder MapSimplifiedMessageStepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/message", ShowMessageEditor)
            .RequireAuthorization()
            .WithOrder(-100);

        app.MapPost("/mailings/{id:guid}/message", SaveMessageAndGoToPayment)
            .RequireAuthorization()
            .WithOrder(-100);

        return app;
    }

    private static IResult ShowMessageEditor(Guid id, HttpContext http, IMailingService mailings, IMessageRenderingService renderer)
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

        return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, renderer, null), authenticated: true));
    }

    private static async Task<IResult> SaveMessageAndGoToPayment(Guid id, HttpContext http, IMailingMessageService messages, IMailingService mailings, IMessageRenderingService renderer)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var existing = mailings.GetForOwner(id, email);
        var form = await http.Request.ReadFormAsync();
        var result = messages.Save(new SaveMailingMessageCommand(
            email,
            id,
            form["senderName"].ToString(),
            form["subject"].ToString(),
            form["body"].ToString(),
            existing?.MessageDraft?.MessageType ?? MessageType.Transactional,
            ToRequestMetadata(http)));

        var mailing = result.Mailing ?? existing;
        if (!result.Ok || mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, renderer, result.Error), authenticated: true));
        }

        return Results.Redirect($"/mailings/{id}/payment");
    }

    private static string MessageForm(Mailing? mailing, IMessageRenderingService renderer, string? error)
    {
        if (mailing is null)
        {
            return HtmlRenderer.Error(error ?? "Рассылка не найдена.");
        }

        var draft = mailing.MessageDraft;
        var preview = renderer.RenderPreview(mailing);
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var senderName = H(draft?.SenderName ?? string.Empty);
        var messageSubject = H(draft?.Subject ?? string.Empty);
        var bodyText = draft?.Body ?? string.Empty;
        var previewSender = string.IsNullOrWhiteSpace(draft?.SenderName) ? "Письмолёт" : H(draft!.SenderName);
        var previewSubject = string.IsNullOrWhiteSpace(draft?.Subject) ? "Тема письма" : H(draft!.Subject);
        var previewBody = string.IsNullOrWhiteSpace(bodyText)
            ? "<p class='muted'>Сохраните текст письма, чтобы увидеть его в превью.</p>"
            : $"<p>{ToHtmlText(bodyText)}</p>";
        var reasonBlock = string.IsNullOrWhiteSpace(preview.ReasonBlock)
            ? "Служебный блок с причиной получения и ссылкой отписки будет добавлен автоматически."
            : H(preview.ReasonBlock);
        var serviceBlock = string.IsNullOrWhiteSpace(preview.ServiceIdentifier)
            ? H($"Служебный идентификатор рассылки: {mailing.PublicId}")
            : H(preview.ServiceIdentifier);
        var unsubscribeUrl = string.IsNullOrWhiteSpace(preview.UnsubscribeUrl) ? "/unsubscribe/example-token" : H(preview.UnsubscribeUrl);

        return $@"
<section class='wizard-shell'>
  {WizardSteps(2)}
  <section class='panel'>
    <div class='topline'>
      <div>
        <p class='eyebrow'>Шаг 2 из 4</p>
        <h1>2. Напишите письмо</h1>
      </div>
      <span class='badge warn'>Письмо</span>
    </div>
    {alert}
    <div class='message-wizard-grid'>
      <form method='post' action='/mailings/{mailing.Id}/message' class='form-grid message-editor-form'>
        <label class='write-field'>
          <span class='field-title'>От кого <span class='required'>*</span></span>
          <input name='senderName' maxlength='{MailingMessageDraft.MaxSenderNameLength}' required value='{senderName}' placeholder='Например: Библиотека №5'>
          <span class='field-hint'>Получатели увидят это имя в письме.</span>
        </label>
        <label>Тема письма
          <input name='subject' maxlength='{MailingMessageDraft.MaxSubjectLength}' required value='{messageSubject}' placeholder='Например: Приглашаем на встречу в субботу'>
        </label>
        <label>Текст письма
          <textarea name='body' rows='12' required placeholder='Здравствуйте!&#10;&#10;Расскажите, почему вы пишете и что нужно сделать получателю.'>{H(bodyText)}</textarea>
        </label>
        <div class='notice warn'>Письмолёт автоматически добавит причину получения письма, ссылку отписки и служебный идентификатор рассылки.</div>
        <div class='actions'>
          <button class='button'>Проверить и оплатить</button>
          <a class='btn ghost' href='/mailings/{mailing.Id}/recipients'>Назад к адресам</a>
        </div>
      </form>
      <aside class='box message-preview-card'>
        <h3>Превью письма</h3>
        <div class='mail-preview'>
          <div class='mail-preview-header'>От: <span>{previewSender}</span> &lt;info@pismolet.ru&gt;</div>
          <div class='mail-preview-body'>
            <h4>{previewSubject}</h4>
            {previewBody}
            <div class='unsubscribe'>
              <p>{reasonBlock}</p>
              <p>Отписаться: <code>{unsubscribeUrl}</code></p>
              <p>{serviceBlock}</p>
            </div>
          </div>
        </div>
      </aside>
    </div>
  </section>
</section>";
    }

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

    private static string ToHtmlText(string value) => H(value)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

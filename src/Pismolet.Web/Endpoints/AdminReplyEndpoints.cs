using System.Net;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminReplyEndpoints
{
    public static IEndpointRouteBuilder MapAdminReplyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/replies", ShowReplies)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult ShowReplies(IReplyEventRepository replies, HttpContext http)
    {
        var status = http.Request.Query["status"].ToString().Trim();
        var items = replies.ListRecent(200);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReplyProcessingStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            items = items.Where(x => x.ProcessingStatus == parsedStatus).ToArray();
        }

        var rows = string.Join(string.Empty, items.Select(ReplyRow));
        if (string.IsNullOrWhiteSpace(rows))
        {
            rows = "<tr><td colspan='8' class='muted'>Reply events не найдены.</td></tr>";
        }

        var body = $@"
<section class='card'>
  <div class='topline'>
    <div>
      <p class='eyebrow'>Admin</p>
      <h1>Ответы получателей</h1>
      <p class='muted'>Диагностика входящих ответов без показа тела письма, raw MIME и служебных token.</p>
    </div>
    <span class='badge neutral'>Reply events</span>
  </div>
  <form method='get' class='form-grid' style='margin:16px 0'>
    <label>Статус
      <select name='status'>
        <option value=''>Все</option>
        {StatusOptions(status)}
      </select>
    </label>
    <div class='actions'><button class='button'>Показать</button><a class='btn secondary' href='/admin/replies'>Сбросить</a></div>
  </form>
  <div class='table-wrap'>
    <table>
      <thead>
        <tr>
          <th>Получен</th>
          <th>Статус</th>
          <th>Рассылка</th>
          <th>Клиент</th>
          <th>Отправитель</th>
          <th>Тема</th>
          <th>Хранение body</th>
          <th>Ошибка</th>
        </tr>
      </thead>
      <tbody>{rows}</tbody>
    </table>
  </div>
</section>";

        return HtmlRenderer.Html(HtmlRenderer.Page("Admin · Ответы получателей", body, authenticated: true));
    }

    private static string ReplyRow(ReplyEvent item)
    {
        var mailing = item.MailingId is null ? string.Empty : $"<a href='/mailings/{item.MailingId}/send'>{item.MailingId}</a>";
        var error = string.IsNullOrWhiteSpace(item.ErrorCode)
            ? string.Empty
            : $"{H(item.ErrorCode)}<br><span class='muted'>{H(item.ErrorMessage)}</span>";
        return $@"
<tr>
  <td>{H(item.ReceivedAt.ToString("yyyy-MM-dd HH:mm"))}</td>
  <td>{H(item.ProcessingStatus.ToRu())}</td>
  <td>{mailing}</td>
  <td>{H(MaskEmail(item.ClientId ?? string.Empty))}</td>
  <td>{H(MaskEmail(item.FromEmailNormalized))}</td>
  <td>{H(item.SubjectPreview)}</td>
  <td>{H(item.BodyStorageStatus.ToString())}</td>
  <td>{error}</td>
</tr>";
    }

    private static string StatusOptions(string selected)
    {
        return string.Join(string.Empty, Enum.GetValues<ReplyProcessingStatus>().Select(status =>
        {
            var value = status.ToString();
            var selectedAttribute = value.Equals(selected, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
            return $"<option value='{H(value)}'{selectedAttribute}>{H(status.ToRu())}</option>";
        }));
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var at = email.IndexOf(Convert.ToChar(64));
        return at <= 1 ? email : $"{email[..1]}***{email[at..]}";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

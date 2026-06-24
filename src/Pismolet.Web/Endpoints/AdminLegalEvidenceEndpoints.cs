using System.Net;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Endpoints;

public static class AdminLegalEvidenceEndpoints
{
    public static IEndpointRouteBuilder MapAdminLegalEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/legal-events", ShowLegalEvents).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult ShowLegalEvents(HttpContext http, LegalEvidenceDbContext db)
    {
        var client = http.Request.Query["client"].ToString().Trim().ToLowerInvariant();
        var eventType = http.Request.Query["event"].ToString().Trim();
        var mailing = http.Request.Query["mailing"].ToString().Trim();
        var limit = ReadLimit(http.Request.Query["limit"].ToString());
        var query = db.LegalEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(client))
        {
            query = query.Where(item => item.ClientId == client);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(item => item.EventType == eventType);
        }

        if (Guid.TryParse(mailing, out var mailingId))
        {
            query = query.Where(item => item.MailingId == mailingId);
        }

        var items = query
            .OrderByDescending(item => item.CreatedAt)
            .Take(limit)
            .ToArray();

        var rows = items.Length == 0
            ? "<tr><td colspan='10'>События не найдены.</td></tr>"
            : string.Join(string.Empty, items.Select(Row));

        var body = $$"""
            <!doctype html>
            <html lang="ru">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Legal evidence</title>
                <style>
                    body{font-family:system-ui,-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;margin:24px;background:#f7f7fb;color:#1b1b1f}
                    a{color:#315efb;text-decoration:none}
                    .panel{background:white;border:1px solid #e6e6ef;border-radius:16px;padding:20px;box-shadow:0 8px 24px rgba(15,23,42,.04)}
                    .filters{display:flex;gap:12px;flex-wrap:wrap;margin:16px 0}.filters label{display:flex;flex-direction:column;font-size:12px;color:#5b5b66}.filters input{padding:8px 10px;border:1px solid #d7d7e0;border-radius:10px}.filters button{align-self:end;padding:9px 14px;border:0;border-radius:10px;background:#1b1b1f;color:white}
                    table{width:100%;border-collapse:collapse;font-size:13px}th,td{border-bottom:1px solid #ececf3;padding:8px;text-align:left;vertical-align:top}th{font-size:12px;color:#6b7280;text-transform:uppercase}.mono{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12px}.muted{color:#6b7280}.wrap{overflow:auto}.meta{max-width:420px;white-space:pre-wrap;word-break:break-word}
                </style>
            </head>
            <body>
                <div class="panel">
                    <p><a href="/admin">← Админка</a></p>
                    <h1>Legal evidence</h1>
                    <p class="muted">Последние юридически значимые события: подтверждение email, декларации базы, согласия и системные подтверждения.</p>
                    <form class="filters" method="get" action="/admin/legal-events">
                        <label>Клиент<input name="client" value="{{H(client)}}" placeholder="email клиента"></label>
                        <label>Событие<input name="event" value="{{H(eventType)}}" placeholder="base_lawfulness_declared"></label>
                        <label>Рассылка<input name="mailing" value="{{H(mailing)}}" placeholder="mailing_id"></label>
                        <label>Лимит<input name="limit" value="{{limit}}"></label>
                        <button type="submit">Показать</button>
                    </form>
                    <div class="wrap">
                        <table>
                            <thead><tr><th>Время</th><th>Событие</th><th>Клиент</th><th>Рассылка</th><th>Импорт</th><th>Документ</th><th>Версия</th><th>Результат</th><th>Маршрут</th><th>Metadata</th></tr></thead>
                            <tbody>{{rows}}</tbody>
                        </table>
                    </div>
                </div>
            </body>
            </html>
            """;

        return Results.Content(body, "text/html; charset=utf-8");
    }

    private static string Row(LegalEventEntity item) => $"""
        <tr>
            <td class="mono">{H(item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"))}</td>
            <td class="mono">{H(item.EventType)}</td>
            <td>{H(item.ClientId)}</td>
            <td class="mono">{H(item.MailingId?.ToString() ?? "")}</td>
            <td class="mono">{H(item.ImportBatchId?.ToString() ?? "")}</td>
            <td class="mono">{H(item.DocumentKey ?? "")}</td>
            <td class="mono">{H(item.DocumentVersion ?? "")}</td>
            <td>{H(item.Result)}</td>
            <td class="mono">{H(item.Route ?? "")}</td>
            <td class="mono meta">{H(item.MetadataJson)}</td>
        </tr>
        """;

    private static int ReadLimit(string value) => int.TryParse(value, out var limit) ? Math.Clamp(limit, 1, 500) : 100;

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

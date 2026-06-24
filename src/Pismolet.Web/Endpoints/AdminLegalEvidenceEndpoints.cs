using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Endpoints;

public static class AdminLegalEvidenceEndpoints
{
    public static IEndpointRouteBuilder MapAdminLegalEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/legal-events", ShowLegalEvents).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/legal-events/export.csv", ExportLegalEventsCsv).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/legal-events/export.json", ExportLegalEventsJson).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/legal-events/{id:guid}", ShowLegalEvent).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult ShowLegalEvents(HttpContext http, [FromServices] LegalEvidenceDbContext db)
    {
        var client = http.Request.Query["client"].ToString().Trim().ToLowerInvariant();
        var eventType = http.Request.Query["event"].ToString().Trim();
        var mailing = http.Request.Query["mailing"].ToString().Trim();
        var limit = ReadLimit(http.Request.Query["limit"].ToString());
        var query = BuildFilteredQuery(db, client, eventType, mailing);

        var items = query
            .OrderByDescending(item => item.CreatedAt)
            .Take(limit)
            .ToArray();

        var rows = items.Length == 0
            ? "<tr><td colspan='11'>События не найдены.</td></tr>"
            : string.Join(string.Empty, items.Select(Row));

        var exportQuery = BuildExportQuery(client, eventType, mailing, limit);
        var csvHref = "/admin/legal-events/export.csv" + exportQuery;
        var jsonHref = "/admin/legal-events/export.json" + exportQuery;

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
                    .exports{display:flex;gap:12px;flex-wrap:wrap;margin:0 0 16px}.exports a{display:inline-block;padding:8px 12px;border:1px solid #d7d7e0;border-radius:10px;background:#f8fafc}
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
                    <div class="exports">
                        <a href="{{H(csvHref)}}">Экспорт CSV</a>
                        <a href="{{H(jsonHref)}}">Экспорт JSON</a>
                    </div>
                    <div class="wrap">
                        <table>
                            <thead><tr><th>Время</th><th>Событие</th><th>Клиент</th><th>Рассылка</th><th>Импорт</th><th>Документ</th><th>Версия</th><th>Результат</th><th>Маршрут</th><th>Metadata</th><th></th></tr></thead>
                            <tbody>{{rows}}</tbody>
                        </table>
                    </div>
                </div>
            </body>
            </html>
            """;

        return Results.Content(body, "text/html; charset=utf-8");
    }

    private static IResult ExportLegalEventsCsv(HttpContext http, [FromServices] LegalEvidenceDbContext db)
    {
        var items = ReadExportItems(http, db);
        var csv = BuildCsv(items);
        return Results.File(
            Encoding.UTF8.GetBytes(csv),
            "text/csv; charset=utf-8",
            $"legal-events-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private static IResult ExportLegalEventsJson(HttpContext http, [FromServices] LegalEvidenceDbContext db)
    {
        var items = ReadExportItems(http, db)
            .Select(item => new
            {
                event_id = item.Id,
                created_at = item.CreatedAt,
                event_type = item.EventType,
                client_id = item.ClientId,
                user_id = item.UserId,
                mailing_id = item.MailingId,
                import_batch_id = item.ImportBatchId,
                document_key = item.DocumentKey,
                document_version = item.DocumentVersion,
                text_hash = item.TextHash,
                result = item.Result,
                ip = item.Ip,
                user_agent = item.UserAgent,
                route = item.Route,
                event_text_snapshot = item.EventTextSnapshot,
                metadata_json = item.MetadataJson
            })
            .ToArray();

        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        return Results.File(
            Encoding.UTF8.GetBytes(json),
            "application/json; charset=utf-8",
            $"legal-events-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    private static IResult ShowLegalEvent(Guid id, [FromServices] LegalEvidenceDbContext db)
    {
        var item = db.LegalEvents.AsNoTracking().FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return Results.NotFound("Legal evidence event not found.");
        }

        var body = $$"""
            <!doctype html>
            <html lang="ru">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Legal evidence event</title>
                <style>
                    body{font-family:system-ui,-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;margin:24px;background:#f7f7fb;color:#1b1b1f}
                    a{color:#315efb;text-decoration:none}.panel{background:white;border:1px solid #e6e6ef;border-radius:16px;padding:20px;box-shadow:0 8px 24px rgba(15,23,42,.04);max-width:1100px}.muted{color:#6b7280}.grid{display:grid;grid-template-columns:220px 1fr;gap:8px 16px}.label{color:#6b7280}.mono{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13px;word-break:break-word}.box{white-space:pre-wrap;word-break:break-word;background:#f8fafc;border:1px solid #e6eaf0;border-radius:12px;padding:14px;margin-top:8px}.section{margin-top:24px}
                </style>
            </head>
            <body>
                <div class="panel">
                    <p><a href="/admin/legal-events">← Legal evidence</a></p>
                    <h1>Карточка legal evidence события</h1>
                    <p class="muted">Полный снимок события для проверки доказательной цепочки.</p>

                    <div class="grid">
                        <div class="label">ID</div><div class="mono">{{H(item.Id.ToString())}}</div>
                        <div class="label">Время</div><div class="mono">{{H(item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"))}}</div>
                        <div class="label">Событие</div><div class="mono">{{H(item.EventType)}}</div>
                        <div class="label">Клиент</div><div>{{H(item.ClientId)}}</div>
                        <div class="label">Пользователь</div><div>{{H(item.UserId ?? "")}}</div>
                        <div class="label">Рассылка</div><div class="mono">{{H(item.MailingId?.ToString() ?? "")}}</div>
                        <div class="label">Импорт</div><div class="mono">{{H(item.ImportBatchId?.ToString() ?? "")}}</div>
                        <div class="label">Документ</div><div class="mono">{{H(item.DocumentKey ?? "")}}</div>
                        <div class="label">Версия документа</div><div class="mono">{{H(item.DocumentVersion ?? "")}}</div>
                        <div class="label">Text hash</div><div class="mono">{{H(item.TextHash ?? "")}}</div>
                        <div class="label">Результат</div><div>{{H(item.Result)}}</div>
                        <div class="label">IP</div><div class="mono">{{H(item.Ip ?? "")}}</div>
                        <div class="label">User-Agent</div><div class="mono">{{H(item.UserAgent ?? "")}}</div>
                        <div class="label">Route</div><div class="mono">{{H(item.Route ?? "")}}</div>
                    </div>

                    <div class="section">
                        <h2>Текст / snapshot</h2>
                        <div class="box">{{H(item.EventTextSnapshot ?? "")}}</div>
                    </div>

                    <div class="section">
                        <h2>Metadata JSON</h2>
                        <div class="box mono">{{H(item.MetadataJson)}}</div>
                    </div>
                </div>
            </body>
            </html>
            """;

        return Results.Content(body, "text/html; charset=utf-8");
    }

    private static LegalEventEntity[] ReadExportItems(HttpContext http, LegalEvidenceDbContext db)
    {
        var client = http.Request.Query["client"].ToString().Trim().ToLowerInvariant();
        var eventType = http.Request.Query["event"].ToString().Trim();
        var mailing = http.Request.Query["mailing"].ToString().Trim();
        var limit = ReadExportLimit(http.Request.Query["limit"].ToString());

        return BuildFilteredQuery(db, client, eventType, mailing)
            .OrderByDescending(item => item.CreatedAt)
            .Take(limit)
            .ToArray();
    }

    private static IQueryable<LegalEventEntity> BuildFilteredQuery(
        LegalEvidenceDbContext db,
        string client,
        string eventType,
        string mailing)
    {
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

        return query;
    }

    private static string BuildCsv(IReadOnlyCollection<LegalEventEntity> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', new[]
        {
            "event_id",
            "created_at",
            "event_type",
            "client_id",
            "user_id",
            "mailing_id",
            "import_batch_id",
            "document_key",
            "document_version",
            "text_hash",
            "result",
            "ip",
            "user_agent",
            "route",
            "event_text_snapshot",
            "metadata_json"
        }));

        foreach (var item in items)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(item.Id.ToString()),
                Csv(item.CreatedAt.ToString("O")),
                Csv(item.EventType),
                Csv(item.ClientId),
                Csv(item.UserId),
                Csv(item.MailingId?.ToString()),
                Csv(item.ImportBatchId?.ToString()),
                Csv(item.DocumentKey),
                Csv(item.DocumentVersion),
                Csv(item.TextHash),
                Csv(item.Result),
                Csv(item.Ip),
                Csv(item.UserAgent),
                Csv(item.Route),
                Csv(item.EventTextSnapshot),
                Csv(item.MetadataJson)
            }));
        }

        return sb.ToString();
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
            <td><a href="/admin/legal-events/{item.Id}">Открыть</a></td>
        </tr>
        """;

    private static string BuildExportQuery(string client, string eventType, string mailing, int limit)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(client))
        {
            parts.Add("client=" + WebUtility.UrlEncode(client));
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            parts.Add("event=" + WebUtility.UrlEncode(eventType));
        }

        if (!string.IsNullOrWhiteSpace(mailing))
        {
            parts.Add("mailing=" + WebUtility.UrlEncode(mailing));
        }

        parts.Add("limit=" + limit.ToString());
        return "?" + string.Join('&', parts);
    }

    private static int ReadLimit(string value) => int.TryParse(value, out var limit) ? Math.Clamp(limit, 1, 500) : 100;

    private static int ReadExportLimit(string value) => int.TryParse(value, out var limit) ? Math.Clamp(limit, 1, 5000) : 1000;

    private static string Csv(string? value) => '"' + (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "\"\"") + '"';

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

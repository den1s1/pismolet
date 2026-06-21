using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Admin;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminSprint10Endpoints
{
    public static IEndpointRouteBuilder MapAdminSprint10Endpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin", Dashboard).RequireAuthorization(AdminEndpoints.AdminPolicyName).WithOrder(-50);
        app.MapGet("/admin/clients", Clients).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/clients/{email}/block", BlockClient).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/clients/{email}/unblock", UnblockClient).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/clients/{email}/limit", UpdateClientLimit).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/clients/{email}/premoderation", UpdateClientPremoderation).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/mailings/{mailingId:guid}/block", BlockMailing).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/mailings/{mailingId:guid}/unblock", UnblockMailing).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/audit", Audit).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/settings/mvp", Settings).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/settings/mvp", SaveSettings).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/imports", Imports).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/complaints", Complaints).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/delivery-errors", DeliveryErrors).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapGet("/admin/replies", Replies).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult Dashboard(HttpContext http, IAdminOperationService admin)
    {
        var email = CurrentEmail(http);
        var snapshot = admin.GetDashboard();
        var auditRows = snapshot.RecentAudit.Count == 0 ? "<li>Пока нет административных действий.</li>" : string.Join("", snapshot.RecentAudit.Select(x => $"<li><b>{H(x.EventType)}</b> — {H(x.User)} — {x.CreatedAt:yyyy-MM-dd HH:mm}</li>"));
        var body = $"""
            <section class='admin-panel'>
              <p class='eyebrow'>Администрирование</p><h1>Операционный dashboard</h1>
              <p class='admin-muted'>Sprint 10: клиенты, лимиты, блокировки, настройки, delivery-сигналы и audit log.</p>
              <div class='admin-stats'>
                {Stat("Клиентов", snapshot.ClientsTotal)}{Stat("Заблокировано", snapshot.ClientsBlocked)}{Stat("Премодерация", snapshot.ClientsPremoderation)}{Stat("Рассылок", snapshot.MailingsTotal)}
                {Stat("На проверке", snapshot.MailingsReviewRequired)}{Stat("Заблокировано рассылок", snapshot.MailingsBlocked)}{Stat("Ошибки отправки", snapshot.MailingsFailed)}{Stat("Жалобы", snapshot.Complaints)}{Stat("Hard bounce", snapshot.HardBounces)}{Stat("Глобально исключены", snapshot.GlobalSuppressions)}
              </div>
              <div class='admin-settings-grid'>
                {Card("Клиенты", "Лимиты, блокировки и премодерация", "/admin/clients")}
                {Card("Рассылки", "Статусы, блокировки, диагностика", "/admin/campaigns")}
                {Card("Импорты", "Сводка accepted/invalid/suppressed", "/admin/imports")}
                {Card("Платежи", "Стоимость и fake payment attempts", "/admin/payments")}
                {Card("Отписки", "Global suppression", "/admin/recipients")}
                {Card("Жалобы", "Complaint-сигналы", "/admin/complaints")}
                {Card("Ошибки доставки", "Soft/hard bounce и rejected", "/admin/delivery-errors")}
                {Card("Ответы", "Inbound replies", "/admin/replies")}
                {Card("Audit log", "История административных действий", "/admin/audit")}
                {Card("Настройки MVP", "Цена, лимиты, премодерация", "/admin/settings/mvp")}
              </div>
              <section class='admin-panel'><h2>Последние действия</h2><ul>{auditRows}</ul></section>
            </section>
            """;
        return AdminHtml("Админка", email, "dashboard", body);
    }

    private static IResult Clients(HttpContext http, IUserRepository users, IMailingRepository mailings)
    {
        var email = CurrentEmail(http);
        var rows = users.ListAll().Take(200).Select(user =>
        {
            var count = mailings.ListForOwner(user.Email).Count;
            var blockAction = user.Profile.IsBlocked
                ? $"<form method='post' action='/admin/clients/{Url(user.Email)}/unblock'><button class='admin-button' type='submit'>Разблокировать</button></form>"
                : $"<form method='post' action='/admin/clients/{Url(user.Email)}/block'><input name='reason' placeholder='Причина'><button class='admin-danger' type='submit'>Заблокировать</button></form>";
            return $"<tr><td><a class='admin-link' href='/admin/users/{Url(user.Email)}'>{H(user.Email)}</a></td><td>{H(ClientStatuses.ToRu(user.Profile.Status))}</td><td>{user.Profile.DailySendLimit}</td><td>{(user.Profile.PremoderationRequired ? "Да" : "Нет")}</td><td>{count}</td><td><form method='post' action='/admin/clients/{Url(user.Email)}/limit'><input name='dailyLimit' type='number' value='{user.Profile.DailySendLimit}' min='0'><button class='admin-button'>Лимит</button></form><form method='post' action='/admin/clients/{Url(user.Email)}/premoderation'><input type='hidden' name='required' value='{(!user.Profile.PremoderationRequired).ToString().ToLowerInvariant()}'><button class='admin-button'>{(user.Profile.PremoderationRequired ? "Выключить премодерацию" : "Включить премодерацию")}</button></form>{blockAction}</td></tr>";
        });
        var body = $"<section class='admin-panel'><h1>Клиенты</h1><p class='admin-muted'>Показаны первые 200 клиентов.</p><table class='admin-table'><thead><tr><th>Email</th><th>Статус</th><th>Дневной лимит</th><th>Премодерация</th><th>Рассылок</th><th>Действия</th></tr></thead><tbody>{string.Join("", rows)}</tbody></table></section>";
        return AdminHtml("Админка - клиенты", email, "clients", body);
    }

    private static async Task<IResult> BlockClient(string email, HttpContext http, IAdminOperationService admin)
    {
        var form = await http.Request.ReadFormAsync();
        var result = admin.BlockClient(email, CurrentEmail(http), form["reason"], Meta(http));
        return RedirectBack(result, $"/admin/users/{Url(email)}");
    }

    private static IResult UnblockClient(string email, HttpContext http, IAdminOperationService admin) => RedirectBack(admin.UnblockClient(email, CurrentEmail(http), Meta(http)), $"/admin/users/{Url(email)}");

    private static async Task<IResult> UpdateClientLimit(string email, HttpContext http, IAdminOperationService admin)
    {
        var form = await http.Request.ReadFormAsync();
        _ = int.TryParse(form["dailyLimit"], out var dailyLimit);
        return RedirectBack(admin.UpdateDailyLimit(email, dailyLimit, CurrentEmail(http), Meta(http)), "/admin/clients");
    }

    private static async Task<IResult> UpdateClientPremoderation(string email, HttpContext http, IAdminOperationService admin)
    {
        var form = await http.Request.ReadFormAsync();
        var required = string.Equals(form["required"], "true", StringComparison.OrdinalIgnoreCase);
        return RedirectBack(admin.SetClientPremoderation(email, required, CurrentEmail(http), Meta(http)), "/admin/clients");
    }

    private static async Task<IResult> BlockMailing(Guid mailingId, HttpContext http, IAdminOperationService admin)
    {
        var form = await http.Request.ReadFormAsync();
        return RedirectBack(admin.BlockMailing(mailingId, CurrentEmail(http), form["reason"], Meta(http)), $"/admin/campaigns/{mailingId}");
    }

    private static IResult UnblockMailing(Guid mailingId, HttpContext http, IAdminOperationService admin) => RedirectBack(admin.UnblockMailing(mailingId, CurrentEmail(http), Meta(http)), $"/admin/campaigns/{mailingId}");

    private static IResult Audit(HttpContext http, IAuditLogger auditLogger)
    {
        var rows = auditLogger.GetRecords().Take(200).Select(x => $"<tr><td>{x.CreatedAt:yyyy-MM-dd HH:mm}</td><td>{H(x.User)}</td><td>{H(x.EventType)}</td><td>{H(Trim(x.Context, 180))}</td><td>{H(x.Ip)}</td></tr>");
        return AdminHtml("Audit log", CurrentEmail(http), "audit", $"<section class='admin-panel'><h1>Audit log</h1><p class='admin-muted'>Последние 200 действий без raw payload и секретов.</p><table class='admin-table'><thead><tr><th>Время</th><th>Actor</th><th>Действие</th><th>Контекст</th><th>IP</th></tr></thead><tbody>{string.Join("", rows)}</tbody></table></section>");
    }

    private static IResult Settings(HttpContext http, IAdminMvpSettingsRepository settingsRepository, IPriceSettingsRepository prices)
    {
        var settings = settingsRepository.Get();
        var price = prices.GetActive();
        var body = $"""
        <section class='admin-panel'><h1>Настройки MVP</h1><p class='admin-muted'>Эти настройки используются backend-сервисами, а не только UI.</p>
        <form class='admin-form' method='post' action='/admin/settings/mvp'>
        <label>Цена письма, RUB <input name='price' type='number' step='0.01' min='0' value='{price.PricePerRecipient}'></label>
        <label>Премодерация новых клиентов <select name='premoderation'><option value='true' {(settings.PremoderationForNewClients ? "selected" : "")}>Включена</option><option value='false' {(!settings.PremoderationForNewClients ? "selected" : "")}>Выключена</option></select></label>
        <label>Дневной лимит нового клиента <input name='dailyLimit' type='number' min='0' value='{settings.DefaultDailySendLimit}'></label>
        <label>Общий лимит нового клиента <input name='totalLimit' type='number' min='0' value='{settings.DefaultTotalSendLimit}'></label>
        <label>Retention тела ответа, дней <input name='replyRetentionDays' type='number' min='1' max='60' value='{settings.ReplyBodyRetentionDays}'></label>
        <button class='admin-button' type='submit'>Сохранить настройки</button></form></section>
        """;
        return AdminHtml("Настройки MVP", CurrentEmail(http), "settings", body);
    }

    private static async Task<IResult> SaveSettings(HttpContext http, IAdminMvpSettingsRepository settingsRepository, IAdminOperationService admin)
    {
        var form = await http.Request.ReadFormAsync();
        var current = settingsRepository.Get();
        var settings = current with
        {
            PricePerRecipient = decimal.TryParse(form["price"], out var p) ? p : current.PricePerRecipient,
            PremoderationForNewClients = string.Equals(form["premoderation"], "true", StringComparison.OrdinalIgnoreCase),
            DefaultDailySendLimit = int.TryParse(form["dailyLimit"], out var d) ? d : current.DefaultDailySendLimit,
            DefaultTotalSendLimit = int.TryParse(form["totalLimit"], out var t) ? t : current.DefaultTotalSendLimit,
            ReplyBodyRetentionDays = int.TryParse(form["replyRetentionDays"], out var r) ? r : current.ReplyBodyRetentionDays
        };
        return RedirectBack(admin.UpdateMvpSettings(settings, CurrentEmail(http), Meta(http)), "/admin/settings/mvp");
    }

    private static IResult Imports(HttpContext http, IUserRepository users, IMailingRepository mailings)
    {
        var rows = users.ListAll().SelectMany(u => mailings.ListForOwner(u.Email)).SelectMany(m => m.ImportBatches.Select(b => $"<tr><td>{H(m.OwnerEmail)}</td><td><a class='admin-link' href='/admin/campaigns/{m.Id}'>{H(m.Subject)}</a></td><td>{b.CreatedAt:yyyy-MM-dd HH:mm}</td><td>{b.TotalRows}</td><td>{b.Accepted}</td><td>{b.Invalid}</td><td>{b.Duplicates}</td><td>{b.GloballySuppressed}</td><td>{b.ClientSuppressed}</td></tr>")).Take(200);
        return AdminHtml("Импорты", CurrentEmail(http), "imports", $"<section class='admin-panel'><h1>Импорты</h1><table class='admin-table'><thead><tr><th>Клиент</th><th>Рассылка</th><th>Дата</th><th>Всего</th><th>Accepted</th><th>Invalid</th><th>Дубли</th><th>Global</th><th>Client</th></tr></thead><tbody>{string.Join("", rows)}</tbody></table></section>");
    }

    private static IResult Complaints(HttpContext http, IUserRepository users, IMailingRepository mailings, ISendEventRepository sends) => DeliveryList(http, users, mailings, sends, "Жалобы", e => e.DeliveryStatus == DeliveryStatus.Complaint);
    private static IResult DeliveryErrors(HttpContext http, IUserRepository users, IMailingRepository mailings, ISendEventRepository sends) => DeliveryList(http, users, mailings, sends, "Ошибки доставки", e => e.DeliveryStatus is DeliveryStatus.SoftBounce or DeliveryStatus.HardBounce or DeliveryStatus.Rejected or DeliveryStatus.Unknown);
    private static IResult Replies(HttpContext http, IReplyEventRepository replies)
    {
        var rows = replies.ListRecent(200).Select(r => $"<tr><td>{r.ReceivedAt:yyyy-MM-dd HH:mm}</td><td>{H(r.ClientId)}</td><td>{r.MailingId}</td><td>{H(r.FromEmailNormalized)}</td><td>{H(r.ProcessingStatus.ToString())}</td></tr>");
        return AdminHtml("Ответы", CurrentEmail(http), "replies", $"<section class='admin-panel'><h1>Ответы</h1><table class='admin-table'><thead><tr><th>Получен</th><th>Клиент</th><th>Рассылка</th><th>От</th><th>Статус</th></tr></thead><tbody>{string.Join("", rows)}</tbody></table></section>");
    }

    private static IResult DeliveryList(HttpContext http, IUserRepository users, IMailingRepository mailings, ISendEventRepository sends, string title, Func<SendEvent, bool> predicate)
    {
        var rows = users.ListAll().SelectMany(u => mailings.ListForOwner(u.Email)).SelectMany(m => sends.ListByMailingId(m.Id).Where(predicate).Select(e => $"<tr><td>{H(m.OwnerEmail)}</td><td><a class='admin-link' href='/admin/campaigns/{m.Id}'>{H(m.Subject)}</a></td><td>{H(e.DeliveryStatus.ToString())}</td><td>{e.UpdatedAt:yyyy-MM-dd HH:mm}</td></tr>")).Take(200);
        return AdminHtml(title, CurrentEmail(http), "delivery", $"<section class='admin-panel'><h1>{H(title)}</h1><p class='admin-muted'>Raw provider payload не отображается.</p><table class='admin-table'><thead><tr><th>Клиент</th><th>Рассылка</th><th>Событие</th><th>Дата</th></tr></thead><tbody>{string.Join("", rows)}</tbody></table></section>");
    }

    private static IResult RedirectBack(AdminActionResult result, string url) => Results.Redirect(result.Ok ? url : $"{url}?error={Url(result.Error)}");
    private static RequestMetadata Meta(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", http.Request.Headers.UserAgent.ToString());
    private static string CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email) ?? "admin@example.test";
    private static string AdminShell(string adminEmail, string content) => $"<section class='admin-shell'><aside class='admin-sidebar'><a class='admin-brand' href='/admin'><span>П</span><b>Письмолёт</b></a><div class='admin-current'><small>Администратор</small><strong>{H(adminEmail)}</strong></div><nav class='admin-nav'><a class='admin-nav-link' href='/admin'>Dashboard</a><a class='admin-nav-link' href='/admin/clients'>Клиенты</a><a class='admin-nav-link' href='/admin/campaigns'>Рассылки</a><a class='admin-nav-link' href='/admin/imports'>Импорты</a><a class='admin-nav-link' href='/admin/payments'>Платежи</a><a class='admin-nav-link' href='/admin/recipients'>Отписки</a><a class='admin-nav-link' href='/admin/complaints'>Жалобы</a><a class='admin-nav-link' href='/admin/delivery-errors'>Ошибки доставки</a><a class='admin-nav-link' href='/admin/replies'>Ответы</a><a class='admin-nav-link' href='/admin/audit'>Audit log</a><a class='admin-nav-link' href='/admin/settings/mvp'>Настройки</a></nav></aside><div class='admin-content'>{content}</div></section>";
    private static IResult AdminHtml(string title, string adminEmail, string active, string body) => HtmlRenderer.Html(HtmlRenderer.Page(title, AdminShell(adminEmail, body), authenticated: true));
    private static string Stat(string name, int value) => $"<div class='admin-stat'><b>{value}</b><span>{H(name)}</span></div>";
    private static string Card(string title, string text, string href) => $"<section class='admin-settings-card'><h2>{H(title)}</h2><p>{H(text)}</p><a class='admin-link' href='{H(href)}'>Открыть</a></section>";
    private static string Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max] + "…";
    private static string Url(string value) => Uri.EscapeDataString(value);
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

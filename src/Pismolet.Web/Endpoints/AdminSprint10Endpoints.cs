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
                {Card("Платежи", "Стоимость и операции оплаты", "/admin/payments")}
                {Card("Отписки", "Global suppression", "/admin/recipients")}
                {Card("Жалобы", "Complaint-сигналы", "/admin/complaints")}
                {Card("Ошибки доставки", "Soft/hard bounce и rejected", "/admin/delivery-errors")}
                {Card("Ответы", "Inbound replies", "/admin/replies")}
                {Card("Audit log", "История административных действий", "/admin/audit")}
                {Card("Настройки", "Warmup, SMTP, лимиты и системные параметры", "/admin/settings")}
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
}

using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminUsersPageEndpoints
{
    public static IEndpointRouteBuilder MapAdminUsersPageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/users", ShowUsers)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        app.MapGet("/admin/clients", () => Results.Redirect("/admin/users"))
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        return app;
    }

    private static IResult ShowUsers(HttpContext http, IUserRepository users, IMailingRepository mailings)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var search = http.Request.Query["q"].ToString().Trim();
        var status = http.Request.Query["status"].ToString().Trim();
        var userList = users.ListAll()
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mailingCounts = mailings.CountByOwners(userList.Select(user => user.Email));
        var rows = userList
            .Select(user => new AdminUserRow(user, mailingCounts.GetValueOrDefault(user.Email)))
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(row =>
                row.User.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.User.Email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.User.Phone.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        rows = status switch
        {
            "premoderation" => rows.Where(row => row.User.Profile.PremoderationRequired),
            "blocked" => rows.Where(row => row.User.Profile.IsBlocked),
            _ => rows
        };

        var filtered = rows.ToArray();
        var tableRows = filtered.Length == 0
            ? "<tr><td colspan='6'>Пользователи не найдены.</td></tr>"
            : string.Join(string.Empty, filtered.Select(UserRow));

        var body = $"""
            <section class='admin-panel admin-users-page'>
                <div class='admin-title-row compact-title'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Пользователи</h1>
                    </div>
                    <a class='admin-export compact-action' href='/admin/users?export=csv'>Экспорт CSV - скоро</a>
                </div>
                <form class='admin-filters compact-filters' method='get' action='/admin/users'>
                    <label>Поиск<input name='q' value='{H(search)}' placeholder='ФИО, email или телефон'></label>
                    <label>Статус
                        <select name='status'>
                            {Option("", "Все", status)}
                            {Option("premoderation", "Премодерация", status)}
                            {Option("blocked", "Заблокирован", status)}
                        </select>
                    </label>
                    <button class='admin-button compact-action' type='submit'>Найти</button>
                    <a class='admin-link compact-reset' href='/admin/users'>Сбросить</a>
                </form>
                <div class='admin-table-wrap'>
                    <table class='admin-table compact-table'>
                        <thead><tr><th>ФИО</th><th>Email</th><th>Телефон</th><th>Статус</th><th>Лимит</th><th>Кампании</th></tr></thead>
                        <tbody>{tableRows}</tbody>
                    </table>
                </div>
            </section>
            """;

        return AdminHtml("Админка - пользователи", adminEmail, body);
    }

    private static string UserRow(AdminUserRow row)
    {
        var user = row.User;
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        var phone = string.IsNullOrWhiteSpace(user.Phone) ? "-" : user.Phone;
        return $"<tr><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(user.Email)}'>{H(displayName)}</a></td><td>{H(user.Email)}</td><td>{H(phone)}</td><td><span class='admin-badge'>{H(user.Profile.Status)}</span></td><td>{user.Profile.DailySendLimit}</td><td>{row.MailingCount}</td></tr>";
    }

    private static IResult AdminHtml(string title, string adminEmail, string body) =>
        HtmlRenderer.Html(HtmlRenderer.Page(title, AdminShell(adminEmail, body), authenticated: true));

    private static string AdminShell(string adminEmail, string content) => $"""
        <section class='admin-shell'>
            <aside class='admin-sidebar'>
                <a class='admin-brand' href='/admin'><span>П</span><b>Письмолёт</b></a>
                <div class='admin-current'><small>Администратор</small><strong>{H(adminEmail)}</strong></div>
                <nav class='admin-nav'>
                    <a class='admin-nav-link active' href='/admin/users'>Пользователи</a>
                    <a class='admin-nav-link' href='/admin/recipients'>Получатели</a>
                    <a class='admin-nav-link' href='/admin/campaigns'>Кампании</a>
                    <a class='admin-nav-link' href='/admin/payments'>Оплаты</a>
                    <a class='admin-nav-link' href='/admin/settings'>Настройки</a>
                </nav>
                <div class='admin-sidebar-links'>
                    <a href='/admin/moderation'>Очередь модерации</a>
                    <a href='/admin/limits'>Дневные лимиты</a>
                    <a href='/dashboard'>В ЛК</a>
                </div>
            </aside>
            <div class='admin-content'>{content}</div>
        </section>
        """;

    private static string Option(string value, string text, string selected) =>
        $"<option value='{H(value)}'{(value == selected ? " selected" : string.Empty)}>{H(text)}</option>";

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private sealed record AdminUserRow(UserAccount User, int MailingCount);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Admin;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
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

        app.MapGet("/admin/users/{email}", ShowUserProfile)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        app.MapPost("/admin/users/{email}/admin/grant", GrantAdmin)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        app.MapPost("/admin/users/{email}/admin/revoke", RevokeAdmin)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        app.MapPost("/admin/users/{email}/notifications", SaveNotificationSettings)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        app.MapPost("/admin/users/{email}/remove", RemoveUser)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        app.MapGet("/admin/clients", () => Results.Redirect("/admin/users"))
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-100);

        return app;
    }

    private static IResult ShowUsers(HttpContext http, IUserRepository users, IMailingRepository mailings, IAdminAccessService admins)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var search = http.Request.Query["q"].ToString().Trim();
        var status = http.Request.Query["status"].ToString().Trim();
        var action = http.Request.Query["action"].ToString().Trim();
        var removed = http.Request.Query["removed"].ToString().Trim();
        var alertHtml = action == "removed"
            ? $"<p class='admin-alert'>Пользователь {H(removed)} и его рассылки удалены.</p>"
            : string.Empty;
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
                (!string.IsNullOrWhiteSpace(row.User.Phone) && row.User.Phone.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        rows = status switch
        {
            "premoderation" => rows.Where(row => row.User.Profile.PremoderationRequired),
            "blocked" => rows.Where(row => row.User.Profile.IsBlocked),
            "admin" => rows.Where(row => admins.IsAdminEmail(row.User.Email)),
            _ => rows
        };

        var filtered = rows.ToArray();
        var tableRows = filtered.Length == 0
            ? "<tr><td colspan='7'>Пользователи не найдены.</td></tr>"
            : string.Join(string.Empty, filtered.Select(row => UserRow(row, admins)));

        var body = $"""
            <section class='admin-panel admin-users-page'>
                <div class='admin-title-row compact-title'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Пользователи</h1>
                    </div>
                    <a class='admin-export compact-action' href='/admin/users?export=csv'>Экспорт CSV - скоро</a>
                </div>
                {alertHtml}
                <form class='admin-filters compact-filters' method='get' action='/admin/users'>
                    <label>Поиск<input name='q' value='{H(search)}' placeholder='ФИО, email или телефон'></label>
                    <label>Статус
                        <select name='status'>
                            {Option("", "Все", status)}
                            {Option("admin", "Администраторы", status)}
                            {Option("premoderation", "Премодерация", status)}
                            {Option("blocked", "Заблокирован", status)}
                        </select>
                    </label>
                    <button class='admin-button compact-action' type='submit'>Найти</button>
                    <a class='admin-link compact-reset' href='/admin/users'>Сбросить</a>
                </form>
                <div class='admin-table-wrap'>
                    <table class='admin-table compact-table'>
                        <thead><tr><th>ФИО</th><th>Email</th><th>Телефон</th><th>Роль</th><th>Статус</th><th>Лимит</th><th>Кампании</th></tr></thead>
                        <tbody>{tableRows}</tbody>
                    </table>
                </div>
            </section>
            """;

        return AdminHtml("Админка - пользователи", adminEmail, body);
    }

    private static IResult ShowUserProfile(string email, HttpContext http, IUserRepository users, IMailingRepository mailings, IAdminAccessService admins, IAdminNotificationSettingsRepository notificationSettings)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var targetEmail = Uri.UnescapeDataString(email);
        var user = users.GetByEmail(targetEmail);
        if (user is null)
        {
            return AdminHtml("Админка - пользователь", adminEmail, HtmlRenderer.Error("Пользователь не найден."));
        }

        var userMailings = ListUserMailings(mailings, user.Email);
        return AdminHtml("Админка - пользователь", adminEmail, UserProfilePage(user, userMailings, adminEmail, admins, notificationSettings.Get(user.Email), null));
    }

    private static IResult GrantAdmin(string email, HttpContext http, IUserRepository users, IMailingRepository mailings, IAdminAccessService admins)
    {
        var adminEmail = CurrentEmail(http) ?? string.Empty;
        var user = users.GetByEmail(Uri.UnescapeDataString(email));
        if (user is null)
        {
            return AdminHtml("Админка - пользователь", adminEmail, HtmlRenderer.Error("Пользователь не найден."));
        }

        admins.GrantAdmin(user.Email, adminEmail);
        return Results.Redirect($"/admin/users/{Uri.EscapeDataString(user.Email)}");
    }

    private static IResult RevokeAdmin(string email, HttpContext http, IUserRepository users, IMailingRepository mailings, IAdminAccessService admins, IAdminNotificationSettingsRepository notificationSettings)
    {
        var adminEmail = CurrentEmail(http) ?? string.Empty;
        var user = users.GetByEmail(Uri.UnescapeDataString(email));
        if (user is null)
        {
            return AdminHtml("Админка - пользователь", adminEmail, HtmlRenderer.Error("Пользователь не найден."));
        }

        if (!admins.TryRevokeAdmin(user.Email, adminEmail, out var error))
        {
            var userMailings = ListUserMailings(mailings, user.Email);
            return AdminHtml("Админка - пользователь", adminEmail, UserProfilePage(user, userMailings, adminEmail, admins, notificationSettings.Get(user.Email), error));
        }

        return Results.Redirect($"/admin/users/{Uri.EscapeDataString(user.Email)}");
    }

    private static async Task<IResult> SaveNotificationSettings(string email, HttpContext http, IUserRepository users, IMailingRepository mailings, IAdminAccessService admins, IAdminNotificationSettingsRepository notificationSettings)
    {
        var adminEmail = CurrentEmail(http) ?? string.Empty;
        var user = users.GetByEmail(Uri.UnescapeDataString(email));
        if (user is null)
        {
            return AdminHtml("Админка - пользователь", adminEmail, HtmlRenderer.Error("Пользователь не найден."));
        }

        var userMailings = ListUserMailings(mailings, user.Email);
        if (!admins.IsAdminEmail(user.Email))
        {
            return AdminHtml("Админка - пользователь", adminEmail, UserProfilePage(user, userMailings, adminEmail, admins, notificationSettings.Get(user.Email), "Настройки уведомлений доступны только администраторам."));
        }

        var form = await http.Request.ReadFormAsync();
        notificationSettings.Save(user.Email, new AdminNotificationSettings(
            UserRegistered: IsChecked(form, "notifyUserRegistered"),
            MailingCreated: IsChecked(form, "notifyMailingCreated"),
            MailingSubmittedToModeration: IsChecked(form, "notifyMailingSubmittedToModeration"),
            MailingPaid: IsChecked(form, "notifyMailingPaid")));

        return Results.Redirect($"/admin/users/{Uri.EscapeDataString(user.Email)}");
    }

    private static IResult RemoveUser(string email, HttpContext http, IUserRepository users, IMailingRepository mailings, IAdminAccessService admins, IAdminNotificationSettingsRepository notificationSettings, IAdminUserRemovalService remover)
    {
        var adminEmail = CurrentEmail(http) ?? string.Empty;
        var user = users.GetByEmail(Uri.UnescapeDataString(email));
        if (user is null)
        {
            return AdminHtml("Админка - пользователь", adminEmail, HtmlRenderer.Error("Пользователь не найден."));
        }

        var userMailings = ListUserMailings(mailings, user.Email);
        if (SameEmail(user.Email, adminEmail))
        {
            return AdminHtml("Админка - пользователь", adminEmail, UserProfilePage(user, userMailings, adminEmail, admins, notificationSettings.Get(user.Email), "Нельзя удалить собственный аккаунт администратора."));
        }

        if (admins.IsConfigAdminEmail(user.Email))
        {
            return AdminHtml("Админка - пользователь", adminEmail, UserProfilePage(user, userMailings, adminEmail, admins, notificationSettings.Get(user.Email), "Нельзя удалить администратора, заданного в конфигурации сервера."));
        }

        if (admins.IsManagedAdminEmail(user.Email))
        {
            return AdminHtml("Админка - пользователь", adminEmail, UserProfilePage(user, userMailings, adminEmail, admins, notificationSettings.Get(user.Email), "Сначала снимите админские права, затем удалите пользователя."));
        }

        var result = remover.RemoveUser(user.Email, adminEmail, ToRequestMetadata(http));
        if (!result.Ok)
        {
            return AdminHtml("Админка - пользователь", adminEmail, UserProfilePage(user, userMailings, adminEmail, admins, notificationSettings.Get(user.Email), result.Error));
        }

        return Results.Redirect($"/admin/users?action=removed&removed={Uri.EscapeDataString(user.Email)}");
    }

    private static string UserProfilePage(UserAccount user, IReadOnlyCollection<Mailing> userMailings, string currentAdminEmail, IAdminAccessService admins, AdminNotificationSettings notificationSettings, string? error)
    {
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        var phone = string.IsNullOrWhiteSpace(user.Phone) ? "-" : user.Phone;
        var isSelf = SameEmail(user.Email, currentAdminEmail);
        var isConfigAdmin = admins.IsConfigAdminEmail(user.Email);
        var isManagedAdmin = admins.IsManagedAdminEmail(user.Email);
        var isAdmin = isConfigAdmin || isManagedAdmin;
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var adminAction = AdminActionBlock(user.Email, isSelf, isConfigAdmin, isManagedAdmin, isAdmin);
        var notificationBlock = AdminNotificationSettingsBlock(user.Email, isAdmin, notificationSettings);
        var removeAction = RemoveUserBlock(user.Email, isSelf, isConfigAdmin, isManagedAdmin);
        var mailingRows = userMailings.Count == 0
            ? "<tr><td colspan='4'>Рассылок пока нет.</td></tr>"
            : string.Join(string.Empty, userMailings.Select(MailingRow));

        return $"""
            <section class='admin-panel admin-user-profile'>
                <div class='admin-title-row compact-title'>
                    <div>
                        <p class='eyebrow'>Профиль пользователя</p>
                        <h1>{H(displayName)}</h1>
                        <p class='muted'>{H(user.Email)}</p>
                    </div>
                    <a class='admin-link compact-reset' href='/admin/users'>← Все пользователи</a>
                </div>
                {alert}
                <div class='split-grid'>
                    <section class='box muted-box'>
                        <h2>Данные</h2>
                        <dl class='cost-list'>
                            <div><dt>Email</dt><dd>{H(user.Email)}</dd></div>
                            <div><dt>Телефон</dt><dd>{H(phone)}</dd></div>
                            <div><dt>Статус</dt><dd>{H(user.Profile.Status)}</dd></div>
                            <div><dt>Премодерация</dt><dd>{(user.Profile.PremoderationRequired ? "Да" : "Нет")}</dd></div>
                            <div><dt>Дневной лимит</dt><dd>{user.Profile.DailySendLimit}</dd></div>
                            <div><dt>Кампании</dt><dd>{userMailings.Count}</dd></div>
                        </dl>
                    </section>
                    <section class='box'>
                        <h2>Админские права</h2>
                        <p>{AdminBadge(user.Email, admins)}</p>
                        <p class='muted'>Администраторы могут открывать админку и отправлять свои рассылки без оплаты.</p>
                        {AdminSourceNote(isConfigAdmin, isManagedAdmin)}
                        {adminAction}
                        {notificationBlock}
                    </section>
                </div>
                <section class='box admin-user-mailings'>
                    <h2>Рассылки пользователя</h2>
                    <div class='admin-table-wrap'>
                        <table class='admin-table compact-table'>
                            <thead><tr><th>Название</th><th>Писем</th><th>Статус</th><th></th></tr></thead>
                            <tbody>{mailingRows}</tbody>
                        </table>
                    </div>
                </section>
                <section class='box'>
                    <h2>Опасная зона</h2>
                    <p class='notice warn'>Удаление уберёт аккаунт пользователя и его рассылки. Действие необратимо.</p>
                    {removeAction}
                </section>
            </section>
            """;
    }

    private static Mailing[] ListUserMailings(IMailingRepository mailings, string ownerEmail) => mailings
        .ListForOwner(ownerEmail)
        .OrderByDescending(mailing => mailing.CreatedAt)
        .ToArray();

    private static string MailingRow(Mailing mailing) =>
        $"<tr><td>{H(mailing.Subject)}</td><td>{mailing.LastImportStats.Accepted}</td><td><span class='admin-badge'>{H(mailing.StatusRu)}</span></td><td><a class='admin-link' href='/admin/campaigns/{mailing.Id}'>Открыть</a></td></tr>";

    private static string AdminActionBlock(string email, bool isSelf, bool isConfigAdmin, bool isManagedAdmin, bool isAdmin)
    {
        var encoded = Uri.EscapeDataString(email);
        if (isSelf && isAdmin)
        {
            return "<p class='notice warn'>С себя снять админские права нельзя.</p>";
        }

        if (isConfigAdmin)
        {
            return "<p class='notice warn'>Этот администратор задан в конфигурации сервера. Снять права можно только через конфиг.</p>";
        }

        if (isManagedAdmin)
        {
            return $"<form method='post' action='/admin/users/{encoded}/admin/revoke'><button class='admin-button danger' type='submit'>Снять админские права</button></form>";
        }

        return $"<form method='post' action='/admin/users/{encoded}/admin/grant'><button class='admin-button' type='submit'>Сделать администратором</button></form>";
    }

    private static string AdminNotificationSettingsBlock(string email, bool isAdmin, AdminNotificationSettings settings)
    {
        if (!isAdmin)
        {
            return string.Empty;
        }

        var encoded = Uri.EscapeDataString(email);
        return $"""
            <form method='post' action='/admin/users/{encoded}/notifications' class='admin-notification-settings'>
                <h3>Уведомления на email</h3>
                <p class='muted'>Настройка индивидуальна для этого администратора. По умолчанию все уведомления выключены.</p>
                {NotificationCheckbox("notifyUserRegistered", "Новый пользователь зарегистрирован", settings.UserRegistered)}
                {NotificationCheckbox("notifyMailingCreated", "Пользователь создал новую рассылку", settings.MailingCreated)}
                {NotificationCheckbox("notifyMailingSubmittedToModeration", "Пользователь отправил рассылку на модерацию", settings.MailingSubmittedToModeration)}
                {NotificationCheckbox("notifyMailingPaid", "Пользователь оплатил рассылку", settings.MailingPaid)}
                <button class='admin-button compact-action' type='submit'>Сохранить уведомления</button>
            </form>
            """;
    }

    private static string NotificationCheckbox(string name, string text, bool isChecked) => $"""
        <label class='admin-checkbox-row'>
            <input type='checkbox' name='{H(name)}' value='on'{(isChecked ? " checked" : string.Empty)}>
            <span>{H(text)}</span>
        </label>
        """;

    private static string RemoveUserBlock(string email, bool isSelf, bool isConfigAdmin, bool isManagedAdmin)
    {
        var encoded = Uri.EscapeDataString(email);
        if (isSelf)
        {
            return "<p class='notice warn'>Собственный аккаунт удалить нельзя.</p>";
        }

        if (isConfigAdmin)
        {
            return "<p class='notice warn'>Пользователь задан администратором в конфигурации сервера. Удаление заблокировано.</p>";
        }

        if (isManagedAdmin)
        {
            return "<p class='notice warn'>Перед удалением снимите админские права.</p>";
        }

        return $"<form method='post' action='/admin/users/{encoded}/remove' onsubmit=\"return confirm('Удалить пользователя и его данные? Это действие необратимо.');\"><button class='admin-button danger' type='submit'>Удалить пользователя и данные</button></form>";
    }

    private static string AdminSourceNote(bool isConfigAdmin, bool isManagedAdmin)
    {
        if (isConfigAdmin)
        {
            return "<p class='muted'>Источник прав: конфигурация сервера.</p>";
        }

        if (isManagedAdmin)
        {
            return "<p class='muted'>Источник прав: назначено через админку.</p>";
        }

        return "<p class='muted'>Админские права не назначены.</p>";
    }

    private static string UserRow(AdminUserRow row, IAdminAccessService admins)
    {
        var user = row.User;
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        var phone = string.IsNullOrWhiteSpace(user.Phone) ? "-" : user.Phone;
        return $"<tr><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(user.Email)}'>{H(displayName)}</a></td><td>{H(user.Email)}</td><td>{H(phone)}</td><td>{AdminBadge(user.Email, admins)}</td><td><span class='admin-badge'>{H(user.Profile.Status)}</span></td><td>{user.Profile.DailySendLimit}</td><td>{row.MailingCount}</td></tr>";
    }

    private static string AdminBadge(string email, IAdminAccessService admins)
    {
        if (admins.IsConfigAdminEmail(email))
        {
            return "<span class='admin-badge'>Администратор · конфиг</span>";
        }

        if (admins.IsManagedAdminEmail(email))
        {
            return "<span class='admin-badge'>Администратор</span>";
        }

        return "<span class='admin-badge muted'>Пользователь</span>";
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

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static bool IsChecked(IFormCollection form, string name) => string.Equals(form[name].ToString(), "on", StringComparison.OrdinalIgnoreCase);

    private static bool SameEmail(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record AdminUserRow(UserAccount User, int MailingCount);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

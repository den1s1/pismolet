using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminEndpoints
{
    public const string AdminPolicyName = "AdminOnly";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin", ShowUsers).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/users", ShowUsers).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/users/{email}", ShowUserProfile).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/recipients", ShowRecipients).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/recipients/{email}", ShowRecipientProfile).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/recipients/{email}/suppress", SuppressRecipient).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/campaigns", ShowCampaigns).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/campaigns/{campaignId:guid}", ShowCampaignProfile).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/campaigns/{campaignId:guid}/pause", (Guid campaignId) => CampaignAction(campaignId, "paused")).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/campaigns/{campaignId:guid}/recheck", (Guid campaignId) => CampaignAction(campaignId, "recheck")).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/campaigns/{campaignId:guid}/moderation", (Guid campaignId) => CampaignAction(campaignId, "moderation")).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/campaigns/{campaignId:guid}/cancel", (Guid campaignId) => CampaignAction(campaignId, "cancelled")).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/payments", (HttpContext http) => Placeholder(http, "Оплаты", "Детальная история оплат появится после подключения биллинга.", "payments")).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/settings", (HttpContext http) => Placeholder(http, "Настройки", "Здесь будут настройки лимитов, цен и правил модерации.", "settings")).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/moderation", ShowQueue).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/moderation/{reviewId:guid}", ShowReview).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/moderation/{reviewId:guid}/approve", Approve).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/moderation/{reviewId:guid}/reject", Reject).RequireAuthorization(AdminPolicyName);
        app.MapGet("/admin/limits", ShowLimits).RequireAuthorization(AdminPolicyName);
        app.MapPost("/admin/limits", UpdateLimit).RequireAuthorization(AdminPolicyName);
        return app;
    }

    private static IResult ShowUsers(HttpContext http, IUserRepository users, IMailingRepository mailings)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var search = http.Request.Query["q"].ToString().Trim();
        var status = http.Request.Query["status"].ToString().Trim();
        var userList = users.ListAll()
            .OrderBy(user => user.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mailingCounts = mailings.CountByOwners(userList.Select(user => user.Email));
        var allRows = userList
            .Select(user => new AdminUserRow(user, mailingCounts.GetValueOrDefault(user.Email)))
            .ToList();

        var rows = allRows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(row =>
                row.User.Email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.User.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        rows = status switch
        {
            "confirmed" => rows.Where(row => row.User.EmailConfirmed),
            "unconfirmed" => rows.Where(row => !row.User.EmailConfirmed),
            "premoderation" => rows.Where(row => row.User.Profile.PremoderationRequired),
            _ => rows
        };

        var filtered = rows.ToList();
        var stats = $"""
            <div class='admin-stats'>
                <div class='admin-stat'><b>{allRows.Count}</b><span>Пользователей</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => row.User.EmailConfirmed)}</b><span>Email подтверждён</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => row.User.Profile.PremoderationRequired)}</b><span>На премодерации</span></div>
                <div class='admin-stat'><b>{allRows.Sum(row => row.MailingCount)}</b><span>Кампаний всего</span></div>
            </div>
            """;

        var tableRows = filtered.Count == 0
            ? "<tr><td colspan='7'>Пользователи не найдены.</td></tr>"
            : string.Join(string.Empty, filtered.Select(UserRow));

        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Пользователи</h1>
                        <p class='admin-muted'>Реальные аккаунты, статусы email, лимиты и количество кампаний.</p>
                    </div>
                    <a class='admin-export' href='/admin/users?export=csv'>Экспорт CSV - скоро</a>
                </div>
                {stats}
                <form class='admin-filters' method='get' action='/admin/users'>
                    <label>Поиск<input name='q' value='{H(search)}' placeholder='email или название клиента'></label>
                    <label>Статус
                        <select name='status'>
                            {Option("", "Все", status)}
                            {Option("confirmed", "Email подтверждён", status)}
                            {Option("unconfirmed", "Email не подтверждён", status)}
                            {Option("premoderation", "Премодерация", status)}
                        </select>
                    </label>
                    <button class='admin-button' type='submit'>Найти</button>
                    <a class='admin-link' href='/admin/users'>Сбросить</a>
                </form>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Email</th><th>Клиент</th><th>Статус</th><th>Email</th><th>Лимит</th><th>Кампании</th><th></th></tr></thead>
                        <tbody>{tableRows}</tbody>
                    </table>
                </div>
            </section>
            """;

        return AdminHtml("Админка - пользователи", adminEmail, "users", body);
    }

    private static IResult ShowUserProfile(string email, HttpContext http, IUserRepository users, IMailingRepository mailings)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var user = users.GetByEmail(email);
        if (user is null)
        {
            return AdminHtml("Пользователь не найден", adminEmail, "users", "<section class='admin-panel'><h1>Пользователь не найден</h1><p class='admin-muted'>Проверьте email и вернитесь к списку.</p><p><a class='admin-link' href='/admin/users'>К пользователям</a></p></section>");
        }

        var userMailings = mailings.ListForOwner(user.Email).OrderByDescending(mailing => mailing.CreatedAt).ToArray();
        var mailingRows = userMailings.Length == 0
            ? "<tr><td colspan='4'>Кампаний пока нет.</td></tr>"
            : string.Join(string.Empty, userMailings.Select(mailing => $"<tr><td>{H(mailing.Subject)}</td><td>{mailing.LastImportStats.Accepted}</td><td><span class='admin-badge'>{H(mailing.StatusRu)}</span></td><td><a class='admin-link' href='/admin/campaigns/{mailing.Id}'>Открыть</a></td></tr>"));

        var body = $"""
            <section class='admin-panel'>
                <p class='eyebrow'>Профиль пользователя</p>
                <h1>{H(user.DisplayName)}</h1>
                <div class='admin-profile-grid'>
                    <div><span>Email</span><b>{H(user.Email)}</b></div>
                    <div><span>Подтверждение email</span><b>{(user.EmailConfirmed ? "Подтверждён" : "Не подтверждён")}</b></div>
                    <div><span>Статус аккаунта</span><b>{H(user.Profile.Status)}</b></div>
                    <div><span>Дневной лимит</span><b>{user.Profile.DailySendLimit}</b></div>
                    <div><span>Общий лимит</span><b>{user.Profile.TotalSendLimit}</b></div>
                    <div><span>Премодерация</span><b>{(user.Profile.PremoderationRequired ? "Включена" : "Не требуется")}</b></div>
                </div>
                <div class='section-head'><div><p class='eyebrow'>Кампании</p><h2>Рассылки пользователя</h2></div><a class='admin-link' href='/admin/limits'>Изменить лимит</a></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'><thead><tr><th>Название</th><th>Писем</th><th>Статус</th><th></th></tr></thead><tbody>{mailingRows}</tbody></table>
                </div>
                <p><a class='admin-link' href='/admin/users'>Вернуться к пользователям</a></p>
            </section>
            """;

        return AdminHtml($"Админка - {user.Email}", adminEmail, "users", body);
    }

    private static IResult ShowRecipients(HttpContext http, IAdminRecipientRepository recipients)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var search = http.Request.Query["q"].ToString().Trim();
        var status = http.Request.Query["status"].ToString().Trim();
        var allRows = recipients.ListSummaries().ToArray();
        var rows = allRows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(row => row.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            rows = rows.Where(row => row.StatusCode == status);
        }

        var filtered = rows.ToArray();
        var stats = $"""
            <div class='admin-stats'>
                <div class='admin-stat'><b>{allRows.Length}</b><span>Получателей</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => row.StatusCode == "active")}</b><span>Активных</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => row.StatusCode is "unsubscribed" or "blocked")}</b><span>Глобально исключены</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => row.StatusCode is "hard_bounce" or "unavailable")}</b><span>Недоступны</span></div>
            </div>
            """;
        var tableRows = filtered.Length == 0
            ? "<tr><td colspan='7'>Получатели не найдены.</td></tr>"
            : string.Join(string.Empty, filtered.Select(RecipientRow));
        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Получатели</h1>
                        <p class='admin-muted'>Глобальные статусы адресов, отписки и история появления в списках клиентов.</p>
                    </div>
                    <a class='admin-export' href='/admin/recipients?export=csv'>Экспорт CSV - скоро</a>
                </div>
                {stats}
                <form class='admin-filters' method='get' action='/admin/recipients'>
                    <label>Поиск<input name='q' value='{H(search)}' placeholder='email получателя'></label>
                    <label>Статус
                        <select name='status'>
                            {Option("", "Все", status)}
                            {Option("active", "Активен", status)}
                            {Option("unsubscribed", "Отписался", status)}
                            {Option("unavailable", "Недоступен", status)}
                            {Option("hard_bounce", "Hard bounce", status)}
                            {Option("blocked", "Заблокирован вручную", status)}
                        </select>
                    </label>
                    <button class='admin-button' type='submit'>Найти</button>
                    <a class='admin-link' href='/admin/recipients'>Сбросить</a>
                </form>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Email</th><th>Статус</th><th>Списки</th><th>Клиентов</th><th>Писем отправлено</th><th>Последнее письмо</th><th></th></tr></thead>
                        <tbody>{tableRows}</tbody>
                    </table>
                </div>
            </section>
            """;

        return AdminHtml("Админка - получатели", adminEmail, "recipients", body);
    }

    private static IResult ShowRecipientProfile(string email, HttpContext http, IAdminRecipientRepository recipients)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var profile = recipients.GetProfile(email);
        if (profile is null)
        {
            return AdminHtml("Получатель не найден", adminEmail, "recipients", "<section class='admin-panel'><h1>Получатель не найден</h1><p class='admin-muted'>Проверьте email или вернитесь к списку получателей.</p><p><a class='admin-link' href='/admin/recipients'>К получателям</a></p></section>");
        }

        var summary = profile.Summary;
        var ownerRows = profile.Owners.Count == 0
            ? "<tr><td colspan='2'>Адрес пока не найден в клиентских списках.</td></tr>"
            : string.Join(string.Empty, profile.Owners.Select(owner => $"<tr><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(owner.OwnerEmail)}'>{H(owner.OwnerEmail)}</a></td><td>{owner.MailingCount}</td></tr>"));
        var mailingRows = profile.Mailings.Count == 0
            ? "<tr><td colspan='5'>Последних писем нет.</td></tr>"
            : string.Join(string.Empty, profile.Mailings.Select(row => $"<tr><td>{H(row.Subject)}</td><td>{H(row.OwnerEmail)}</td><td>{row.AcceptedRecipients}</td><td><span class='admin-badge'>{H(row.Status.ToRu())}</span></td><td>{FormatDate(row.LastMessageAt ?? row.CreatedAt)}</td></tr>"));
        var suppression = summary.SuppressedAt is null
            ? "Отписки не зафиксированы"
            : $"{H(summary.SuppressionSource?.ToString())} · {FormatDate(summary.SuppressedAt)}";
        var body = $"""
            <section class='admin-panel'>
                <p class='eyebrow'>Профиль получателя</p>
                <h1>{H(summary.Email)}</h1>
                <div class='admin-profile-grid'>
                    <div><span>Глобальный статус</span><b>{H(summary.StatusText)}</b></div>
                    <div><span>Первое появление</span><b>{FormatDate(summary.FirstSeenAt)}</b></div>
                    <div><span>Последнее письмо</span><b>{FormatDate(summary.LastMessageAt)}</b></div>
                    <div><span>Отписки</span><b>{suppression}</b></div>
                </div>
                <div class='admin-actions-row'>
                    <form method='post' action='/admin/recipients/{Uri.EscapeDataString(summary.Email)}/suppress'>
                        <button class='admin-button danger' type='submit'>Добавить глобальную отписку</button>
                    </form>
                    <a class='admin-link' href='/admin/recipients?q={Uri.EscapeDataString(summary.Email)}'>Найти дубли</a>
                    <a class='admin-link' href='/admin/recipients'>К списку</a>
                </div>
                <div class='section-head'><div><p class='eyebrow'>Клиенты</p><h2>Списки пользователей</h2></div></div>
                <div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Пользователь</th><th>Списков</th></tr></thead><tbody>{ownerRows}</tbody></table></div>
                <div class='section-head'><div><p class='eyebrow'>История</p><h2>Последние письма</h2></div></div>
                <div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Кампания</th><th>Пользователь</th><th>Всего адресов</th><th>Статус</th><th>Дата</th></tr></thead><tbody>{mailingRows}</tbody></table></div>
            </section>
            """;

        return AdminHtml($"Админка - {summary.Email}", adminEmail, "recipients", body);
    }

    private static IResult SuppressRecipient(string email, IGlobalSuppressionRepository suppressions)
    {
        suppressions.Add(email);
        return Results.Redirect($"/admin/recipients/{Uri.EscapeDataString(email)}");
    }

    private static IResult ShowCampaigns(HttpContext http, IAdminCampaignRepository campaigns)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var search = http.Request.Query["q"].ToString().Trim();
        var status = http.Request.Query["status"].ToString().Trim();
        var allRows = campaigns.ListSummaries()
            .OrderByDescending(row => row.CreatedAt)
            .ToArray();

        var rows = allRows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(row =>
                row.Subject.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.DisplaySubject.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.OwnerEmail.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.ClientName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            rows = rows.Where(row => string.Equals(row.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = rows.ToArray();
        var stats = $"""
            <div class='admin-stats'>
                <div class='admin-stat'><b>{allRows.Length}</b><span>Кампаний</span></div>
                <div class='admin-stat'><b>{allRows.Sum(row => row.AcceptedRecipients)}</b><span>Получателей к отправке</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => row.StatusRu.Contains("провер", StringComparison.OrdinalIgnoreCase))}</b><span>На проверке</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => row.StatusRu.Contains("отправ", StringComparison.OrdinalIgnoreCase))}</b><span>В отправке</span></div>
            </div>
            """;
        var statusOptions = string.Join(string.Empty, allRows
            .Select(row => row.Status.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => Option(value, value, status)));
        var tableRows = filtered.Length == 0
            ? "<tr><td colspan='7'>Кампании не найдены.</td></tr>"
            : string.Join(string.Empty, filtered.Select(CampaignRow));
        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Кампании</h1>
                        <p class='admin-muted'>Все рассылки сервиса, статусы, стоимость, получатели и быстрые действия модератора.</p>
                    </div>
                    <a class='admin-export' href='/admin/moderation'>Очередь модерации</a>
                </div>
                {stats}
                <form class='admin-filters' method='get' action='/admin/campaigns'>
                    <label>Поиск<input name='q' value='{H(search)}' placeholder='кампания, email или клиент'></label>
                    <label>Статус
                        <select name='status'>
                            {Option("", "Все", status)}
                            {statusOptions}
                        </select>
                    </label>
                    <button class='admin-button' type='submit'>Найти</button>
                    <a class='admin-link' href='/admin/campaigns'>Сбросить</a>
                </form>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Кампания</th><th>Пользователь</th><th>Статус</th><th>Получателей</th><th>Стоимость</th><th>Создана</th><th></th></tr></thead>
                        <tbody>{tableRows}</tbody>
                    </table>
                </div>
            </section>
            """;

        return AdminHtml("Админка - кампании", adminEmail, "campaigns", body);
    }

    private static IResult ShowCampaignProfile(Guid campaignId, HttpContext http, IUserRepository users, IMailingRepository mailings, ISendEventRepository sends)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var mailing = mailings.Get(campaignId);
        if (mailing is null)
        {
            return AdminHtml("Кампания не найдена", adminEmail, "campaigns", "<section class='admin-panel'><h1>Кампания не найдена</h1><p class='admin-muted'>Проверьте идентификатор кампании и вернитесь к списку.</p><p><a class='admin-link' href='/admin/campaigns'>К кампаниям</a></p></section>");
        }

        var client = users.GetByEmail(mailing.OwnerEmail);
        var sendEvents = sends.ListByMailingId(mailing.Id).OrderByDescending(x => x.UpdatedAt).Take(12).ToArray();
        var action = http.Request.Query["action"].ToString();
        var actionMessage = action switch
        {
            "paused" => "Запрос на паузу зафиксирован в UI. Фактическая остановка очереди будет подключена вместе с боевым SMTP-воркером.",
            "recheck" => "Запрос на повторную проверку письма принят.",
            "moderation" => "Кампания помечена для возврата на модерацию в рабочем сценарии.",
            "cancelled" => "Запрос на отмену кампании принят.",
            _ => string.Empty
        };
        var alert = string.IsNullOrWhiteSpace(actionMessage) ? string.Empty : $"<p class='admin-alert'>{H(actionMessage)}</p>";
        var sendRows = sendEvents.Length == 0
            ? "<tr><td colspan='5'>Лог отправки пока пуст.</td></tr>"
            : string.Join(string.Empty, sendEvents.Select(x => $"<tr><td>{H(x.RecipientEmail)}</td><td>{H(x.Status.ToString())}</td><td>{H(x.DeliveryStatus.ToString())}</td><td>{FormatDate(x.UpdatedAt)}</td><td>{H(x.Reason?.ToString())}</td></tr>"));
        var timeline = CampaignTimeline(mailing, sendEvents.Length != 0);
        var bodyText = mailing.MessageDraft?.Body ?? "Письмо ещё не заполнено.";
        var body = $"""
            <section class='admin-panel'>
                <p class='eyebrow'>Профиль кампании</p>
                <h1>{H(mailing.MessageDraft?.Subject ?? mailing.Subject)}</h1>
                {alert}
                <div class='admin-profile-grid'>
                    <div><span>Пользователь</span><b><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(mailing.OwnerEmail)}'>{H(client?.DisplayName ?? mailing.OwnerEmail)}</a></b></div>
                    <div><span>Email клиента</span><b>{H(mailing.OwnerEmail)}</b></div>
                    <div><span>Дата создания</span><b>{FormatDate(mailing.CreatedAt)}</b></div>
                    <div><span>Статус</span><b>{H(mailing.StatusRu)}</b></div>
                    <div><span>Получателей</span><b>{mailing.LastImportStats.Accepted}</b></div>
                    <div><span>Стоимость</span><b>{FormatMoney(mailing.LastImportStats.Accepted)}</b></div>
                    <div><span>Тип письма</span><b>{H(mailing.MessageDraft is null ? "Не указан" : "Указан в письме")}</b></div>
                    <div><span>Отправитель</span><b>{H(mailing.MessageDraft?.SenderName ?? "-")}</b></div>
                </div>
                <div class='admin-actions-row'>
                    <form method='post' action='/admin/campaigns/{mailing.Id}/pause'><button class='admin-button' type='submit'>Поставить на паузу</button></form>
                    <form method='post' action='/admin/campaigns/{mailing.Id}/recheck'><button class='admin-button' type='submit'>Повторно проверить письмо</button></form>
                    <form method='post' action='/admin/campaigns/{mailing.Id}/moderation'><button class='admin-button' type='submit'>Вернуть на модерацию</button></form>
                    <form method='post' action='/admin/campaigns/{mailing.Id}/cancel'><button class='admin-button danger' type='submit'>Отменить кампанию</button></form>
                </div>
                <div class='section-head'><div><p class='eyebrow'>Контроль</p><h2>Таймлайн кампании</h2></div></div>
                <ol class='admin-timeline'>{timeline}</ol>
                <div class='section-head'><div><p class='eyebrow'>Письмо</p><h2>Текст письма</h2></div></div>
                <pre>{H(bodyText)}</pre>
                <div class='section-head'><div><p class='eyebrow'>Отправка</p><h2>Лог отправки</h2></div><a class='admin-link' href='/mailings/{mailing.Id}/send'>Открыть клиентский экран</a></div>
                <div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Email</th><th>Событие</th><th>Доставка</th><th>Дата</th><th>Причина</th></tr></thead><tbody>{sendRows}</tbody></table></div>
                <p><a class='admin-link' href='/admin/campaigns'>Вернуться к кампаниям</a></p>
            </section>
            """;

        return AdminHtml($"Админка - {mailing.Subject}", adminEmail, "campaigns", body);
    }

    private static IResult CampaignAction(Guid campaignId, string action) => Results.Redirect($"/admin/campaigns/{campaignId}?action={Uri.EscapeDataString(action)}");

    private static IResult ShowQueue(HttpContext http, IModerationAdminService moderation)
    {
        var items = moderation.ListOpen();
        var rows = items.Count == 0
            ? "<tr><td colspan='5'>Очередь пуста.</td></tr>"
            : string.Join(string.Empty, items.Select(item =>
            {
                var mailing = item.Mailing;
                var reasons = SafeReasons(item.RiskResult);
                return $"<tr><td>{item.Review.CreatedAt:yyyy-MM-dd HH:mm}</td><td>{H(mailing?.MessageDraft?.Subject ?? mailing?.Subject ?? "Без темы")}</td><td>{H(mailing?.OwnerEmail ?? "неизвестно")}</td><td>{H(reasons)}</td><td><a class='admin-link' href='/admin/moderation/{item.Review.Id}'>Открыть</a></td></tr>";
            }));

        var body = $"<section class='admin-panel'><h1>Очередь модерации</h1><p class='admin-muted'>Доступ ограничен администраторами из allowlist.</p><div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Дата</th><th>Тема</th><th>Клиент</th><th>Причины</th><th></th></tr></thead><tbody>{rows}</tbody></table></div></section>";
        return AdminHtml("Очередь модерации", CurrentEmail(http) ?? "admin@example.test", "campaigns", body);
    }

    private static IResult ShowReview(Guid reviewId, HttpContext http, IModerationAdminService moderation, IMessageRenderingService renderer)
    {
        var result = moderation.Get(reviewId);
        return AdminHtml("Карточка модерации", CurrentEmail(http) ?? "admin@example.test", "campaigns", ReviewPage(result, renderer));
    }

    private static async Task<IResult> Approve(Guid reviewId, HttpContext http, IModerationAdminService moderation, IMessageRenderingService renderer)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var form = await http.Request.ReadFormAsync();
        var result = moderation.Approve(reviewId, email, form["comment"].ToString(), ToRequestMetadata(http));
        return AdminHtml("Карточка модерации", email, "campaigns", ReviewPage(result, renderer));
    }

    private static async Task<IResult> Reject(Guid reviewId, HttpContext http, IModerationAdminService moderation, IMessageRenderingService renderer)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var form = await http.Request.ReadFormAsync();
        var result = moderation.Reject(reviewId, email, form["comment"].ToString(), ToRequestMetadata(http));
        return AdminHtml("Карточка модерации", email, "campaigns", ReviewPage(result, renderer));
    }

    private static IResult ShowLimits(HttpContext http) => AdminHtml("Дневные лимиты", CurrentEmail(http) ?? "admin@example.test", "settings", LimitPage(null, null, null));

    private static async Task<IResult> UpdateLimit(HttpContext http, IClientSendLimitAdminService limits)
    {
        var adminEmail = CurrentEmail(http);
        if (adminEmail is null) return Results.Redirect("/account/login");
        var form = await http.Request.ReadFormAsync();
        var clientEmail = form["clientEmail"].ToString();
        var rawLimit = form["dailyLimit"].ToString();
        if (!int.TryParse(rawLimit, out var dailyLimit))
        {
            return AdminHtml("Дневные лимиты", adminEmail, "settings", LimitPage("Укажите лимит числом.", clientEmail, rawLimit));
        }

        var result = limits.UpdateDailyLimit(clientEmail, dailyLimit, adminEmail, ToRequestMetadata(http));
        var message = result.Ok && result.User is not null
            ? $"Лимит клиента {result.User.Email} изменён на {result.User.Profile.DailySendLimit}."
            : result.Error;
        return AdminHtml("Дневные лимиты", adminEmail, "settings", LimitPage(message, clientEmail, dailyLimit.ToString()));
    }

    private static IResult Placeholder(HttpContext http, string title, string text, string active)
    {
        var body = $"<section class='admin-panel'><p class='eyebrow'>Админка</p><h1>{H(title)}</h1><p class='admin-muted'>{H(text)}</p><p><a class='admin-link' href='/admin/users'>К пользователям</a></p></section>";
        return AdminHtml($"Админка - {title}", CurrentEmail(http) ?? "admin@example.test", active, body);
    }

    private static string ReviewPage(AdminModerationResult result, IMessageRenderingService renderer)
    {
        if (!result.Ok || result.Review is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var review = result.Review;
        var mailing = result.Mailing;
        var risk = result.RiskResult;
        var preview = mailing is null ? null : renderer.RenderPreview(mailing);
        var resolvedAt = review.ResolvedAt?.ToString("yyyy-MM-dd HH:mm") ?? "не указано";
        var rules = risk is null || risk.TriggeredRules.Count == 0
            ? "<li>Формальная проверка не указала дополнительные причины.</li>"
            : string.Join(string.Empty, risk.TriggeredRules.Select(rule => $"<li>{H(rule.PublicReason)} - {rule.Score} баллов</li>"));
        var actions = review.Status == ModerationReviewStatus.Open
            ? $"<form method='post' action='/admin/moderation/{review.Id}/approve'><label>Комментарий модератора<input name='comment'></label><button class='admin-button'>Одобрить</button></form><form method='post' action='/admin/moderation/{review.Id}/reject'><label>Комментарий модератора<input name='comment'></label><button class='admin-button danger'>Отклонить</button></form>"
            : $"<p><span class='admin-badge'>{review.Status.ToRu()}</span></p><p class='admin-muted'>Решение: {H(review.ResolvedBy)} · {H(resolvedAt)}</p>";
        var logs = result.Logs.Count == 0
            ? "<li>Действий пока нет.</li>"
            : string.Join(string.Empty, result.Logs.Select(log => $"<li>{log.CreatedAt:yyyy-MM-dd HH:mm}: {H(log.ActorEmail)} - {H(log.Action)} ({H(log.PreviousState)} → {H(log.NewState)})</li>"));

        return $"<section class='admin-panel'><h1>Карточка модерации</h1><p><span class='admin-badge'>{review.Status.ToRu()}</span></p><p><strong>Рассылка:</strong> {H(mailing?.Subject ?? "не найдена")}</p><p><strong>Клиент:</strong> {H(mailing?.OwnerEmail ?? "неизвестно")}</p><p><strong>Причина ручной проверки:</strong> {H(review.Reason)}</p><h2>Письмо</h2><p><strong>Отправитель:</strong> {H(mailing?.MessageDraft?.SenderName)}</p><p><strong>Тема:</strong> {H(mailing?.MessageDraft?.Subject)}</p><pre>{H(mailing?.MessageDraft?.Body)}</pre><h2>Служебные блоки</h2><p>{H(preview?.ReasonBlock)}</p><p>{H(preview?.UnsubscribeUrl)}</p><p>{H(preview?.ServiceIdentifier)}</p><h2>Формальные причины</h2><ul>{rules}</ul><h2>Решение</h2>{actions}<h2>Лог действий</h2><ul>{logs}</ul><p><a class='admin-link' href='/admin/moderation'>Вернуться к очереди</a></p></section>";
    }

    private static string LimitPage(string? message, string? clientEmail, string? dailyLimit)
    {
        var alert = string.IsNullOrWhiteSpace(message) ? string.Empty : $"<p class='admin-muted'>{H(message)}</p>";
        return $"<section class='admin-panel form-card'><h1>Дневные лимиты клиентов</h1><p class='admin-muted'>Dev/admin форма. Изменение влияет на новые запуски и resume, но не переписывает уже созданные события отправки.</p>{alert}<form method='post' action='/admin/limits'><label>Email клиента<input type='email' name='clientEmail' required value='{H(clientEmail)}'></label><label>Новый дневной лимит<input type='number' min='0' max='100000' name='dailyLimit' required value='{H(dailyLimit)}'></label><button class='admin-button'>Сохранить лимит</button></form></section>";
    }

    private static string UserRow(AdminUserRow row)
    {
        var user = row.User;
        return $"<tr><td>{H(user.Email)}</td><td>{H(user.DisplayName)}</td><td><span class='admin-badge'>{H(user.Profile.Status)}</span></td><td>{(user.EmailConfirmed ? "Подтверждён" : "Не подтверждён")}</td><td>{user.Profile.DailySendLimit}</td><td>{row.MailingCount}</td><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(user.Email)}'>Профиль</a></td></tr>";
    }

    private static string RecipientRow(AdminRecipientSummary row) => $"<tr><td>{H(row.Email)}</td><td><span class='admin-badge recipient-{H(row.StatusCode)}'>{H(row.StatusText)}</span></td><td>{row.MailingCount}</td><td>{row.OwnerCount}</td><td>{row.SentCount}</td><td>{FormatDate(row.LastMessageAt)}</td><td><a class='admin-link' href='/admin/recipients/{Uri.EscapeDataString(row.Email)}'>Профиль</a></td></tr>";

    private static string CampaignRow(AdminMailingSummary row) => $"<tr><td>{H(row.DisplaySubject)}</td><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(row.OwnerEmail)}'>{H(row.ClientName)}</a><br><span class='admin-muted'>{H(row.OwnerEmail)}</span></td><td><span class='admin-badge'>{H(row.StatusRu)}</span></td><td>{row.AcceptedRecipients}</td><td>{FormatMoney(row.AcceptedRecipients)}</td><td>{FormatDate(row.CreatedAt)}</td><td><a class='admin-link' href='/admin/campaigns/{row.Id}'>Открыть</a></td></tr>";

    private static string CampaignTimeline(Mailing mailing, bool hasSendEvents)
    {
        var accepted = mailing.LastImportStats.Accepted > 0;
        var hasMessage = mailing.MessageDraft is not null;
        var status = mailing.StatusRu;
        var paid = status.Contains("оплат", StringComparison.OrdinalIgnoreCase) || status.Contains("очеред", StringComparison.OrdinalIgnoreCase) || status.Contains("отправ", StringComparison.OrdinalIgnoreCase);
        return $"<li>Создана: {FormatDate(mailing.CreatedAt)}</li>" +
               $"<li>{(accepted ? "Адреса проверены" : "Адреса ещё не загружены")}</li>" +
               $"<li>{(hasMessage ? "Письмо подготовлено" : "Письмо ещё не подготовлено")}</li>" +
               $"<li>{(paid ? "Оплата пройдена или ожидается" : "Оплата ещё не завершена")}</li>" +
               $"<li>{(hasSendEvents ? "Отправка начата" : "Отправка ещё не начиналась")}</li>";
    }

    private static string FormatMoney(int acceptedRecipients) => $"{acceptedRecipients} ₽";

    private static string AdminShell(string adminEmail, string active, string content) => $"""
        <section class='admin-shell'>
            <aside class='admin-sidebar'>
                <a class='admin-brand' href='/admin'><span>П</span><b>Письмолёт</b></a>
                <div class='admin-current'><small>Администратор</small><strong>{H(adminEmail)}</strong></div>
                <nav class='admin-nav'>
                    {AdminNavLink("users", "/admin/users", "Пользователи", active)}
                    {AdminNavLink("recipients", "/admin/recipients", "Получатели", active)}
                    {AdminNavLink("campaigns", "/admin/campaigns", "Кампании", active)}
                    {AdminNavLink("payments", "/admin/payments", "Оплаты", active)}
                    {AdminNavLink("settings", "/admin/settings", "Настройки", active)}
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

    private static IResult AdminHtml(string title, string adminEmail, string active, string body) =>
        HtmlRenderer.Html(HtmlRenderer.Page(title, AdminShell(adminEmail, active, body), authenticated: true));

    private static string AdminNavLink(string key, string href, string text, string active) =>
        $"<a class='admin-nav-link{(key == active ? " active" : string.Empty)}' href='{H(href)}'>{H(text)}</a>";

    private static string Option(string value, string text, string selected) =>
        $"<option value='{H(value)}'{(value == selected ? " selected" : string.Empty)}>{H(text)}</option>";

    private static string SafeReasons(RiskCheckResult? risk) => risk is null || risk.TriggeredRules.Count == 0
        ? "Нет дополнительных причин"
        : string.Join("; ", risk.TriggeredRules.Select(rule => rule.PublicReason).Distinct());

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private sealed record AdminUserRow(UserAccount User, int MailingCount);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

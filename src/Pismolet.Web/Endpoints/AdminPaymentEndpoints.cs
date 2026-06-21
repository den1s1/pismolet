using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminPaymentEndpoints
{
    public static IEndpointRouteBuilder MapAdminPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/payments", ShowPayments)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-1);
        app.MapGet("/admin/payments/{campaignId:guid}", ShowPaymentProfile)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-1);
        app.MapPost("/admin/payments/{campaignId:guid}/reconcile", (Guid campaignId) => Results.Redirect($"/admin/payments/{campaignId}?action=reconcile"))
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-1);
        return app;
    }

    private static IResult ShowPayments(HttpContext http, IAdminPaymentRepository payments)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var search = http.Request.Query["q"].ToString().Trim();
        var status = http.Request.Query["status"].ToString().Trim();
        var allRows = payments.ListSummaries()
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
            rows = rows.Where(row => PaymentStageCode(row) == status);
        }

        var filtered = rows.ToArray();
        var stats = $"""
            <div class='admin-stats'>
                <div class='admin-stat'><b>{allRows.Length}</b><span>Счетов</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => PaymentStageCode(row) == "pending")}</b><span>Ожидают оплаты</span></div>
                <div class='admin-stat'><b>{allRows.Count(row => PaymentStageCode(row) == "paid")}</b><span>Оплачены</span></div>
                <div class='admin-stat'><b>{FormatMoney(allRows.Where(row => PaymentStageCode(row) == "paid").Sum(row => row.AcceptedRecipients))}</b><span>Оплачено всего</span></div>
            </div>
            """;
        var tableRows = filtered.Length == 0
            ? "<tr><td colspan='7'>Платежи не найдены.</td></tr>"
            : string.Join(string.Empty, filtered.Select(PaymentRow));
        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Оплаты</h1>
                        <p class='admin-muted'>Контроль счетов по кампаниям: готовность к оплате, ожидание оплаты, оплаченные рассылки и сумма.</p>
                    </div>
                    <a class='admin-export' href='/admin/payments?export=csv'>Экспорт CSV - скоро</a>
                </div>
                {stats}
                <form class='admin-filters' method='get' action='/admin/payments'>
                    <label>Поиск<input name='q' value='{H(search)}' placeholder='кампания, email или клиент'></label>
                    <label>Статус
                        <select name='status'>
                            {Option("", "Все", status)}
                            {Option("ready", "Готово к оплате", status)}
                            {Option("pending", "Ожидает оплаты", status)}
                            {Option("paid", "Оплачено", status)}
                            {Option("not_ready", "Не готово", status)}
                        </select>
                    </label>
                    <button class='admin-button' type='submit'>Найти</button>
                    <a class='admin-link' href='/admin/payments'>Сбросить</a>
                </form>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Кампания</th><th>Пользователь</th><th>Статус оплаты</th><th>Писем</th><th>Сумма</th><th>Создана</th><th></th></tr></thead>
                        <tbody>{tableRows}</tbody>
                    </table>
                </div>
            </section>
            """;

        return AdminHtml("Админка - оплаты", adminEmail, "payments", body);
    }

    private static IResult ShowPaymentProfile(Guid campaignId, HttpContext http, IUserRepository users, IMailingRepository mailings)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var mailing = mailings.Get(campaignId);
        if (mailing is null)
        {
            return AdminHtml("Платеж не найден", adminEmail, "payments", "<section class='admin-panel'><h1>Платеж не найден</h1><p class='admin-muted'>Проверьте идентификатор кампании и вернитесь к списку оплат.</p><p><a class='admin-link' href='/admin/payments'>К оплатам</a></p></section>");
        }

        var client = users.GetByEmail(mailing.OwnerEmail);
        var action = http.Request.Query["action"].ToString();
        var alert = action == "reconcile"
            ? "<p class='admin-alert'>Запрос на ручную сверку платежа зафиксирован в UI. Боевой биллинг будет подключён отдельным спринтом.</p>"
            : string.Empty;
        var accepted = mailing.LastImportStats.Accepted;
        var excluded = Math.Max(0, mailing.LastImportStats.TotalRows - mailing.LastImportStats.Accepted);
        var eventsRows = PaymentEventsRows(mailing);
        var body = $"""
            <section class='admin-panel'>
                <p class='eyebrow'>Профиль платежа</p>
                <h1>{H(mailing.MessageDraft?.Subject ?? mailing.Subject)}</h1>
                {alert}
                <div class='admin-profile-grid'>
                    <div><span>Пользователь</span><b><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(mailing.OwnerEmail)}'>{H(client?.DisplayName ?? mailing.OwnerEmail)}</a></b></div>
                    <div><span>Email клиента</span><b>{H(mailing.OwnerEmail)}</b></div>
                    <div><span>Статус оплаты</span><b>{H(PaymentStageText(mailing))}</b></div>
                    <div><span>Статус кампании</span><b>{H(mailing.StatusRu)}</b></div>
                    <div><span>Писем к оплате</span><b>{accepted}</b></div>
                    <div><span>Исключено</span><b>{excluded}</b></div>
                    <div><span>Сумма</span><b>{FormatMoney(accepted)}</b></div>
                    <div><span>Дата создания</span><b>{FormatDate(mailing.CreatedAt)}</b></div>
                </div>
                <div class='admin-actions-row'>
                    <a class='admin-link' href='/admin/campaigns/{mailing.Id}'>Открыть кампанию</a>
                    <a class='admin-link' href='/mailings/{mailing.Id}/payment'>Клиентский экран оплаты</a>
                    <form method='post' action='/admin/payments/{mailing.Id}/reconcile'><button class='admin-button' type='submit'>Запросить ручную сверку</button></form>
                </div>
                <div class='section-head'><div><p class='eyebrow'>Биллинг</p><h2>События оплаты</h2></div></div>
                <div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Событие</th><th>Статус</th><th>Сумма</th><th>Дата</th></tr></thead><tbody>{eventsRows}</tbody></table></div>
                <p><a class='admin-link' href='/admin/payments'>Вернуться к оплатам</a></p>
            </section>
            """;

        return AdminHtml($"Админка - оплата {mailing.Subject}", adminEmail, "payments", body);
    }

    private static string PaymentRow(AdminMailingSummary row) =>
        $"<tr><td><a class='admin-link' href='/admin/payments/{row.Id}'>{H(row.DisplaySubject)}</a><br><span class='admin-muted'>{H(row.Id.ToString())}</span></td><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(row.OwnerEmail)}'>{H(row.ClientName)}</a><br><span class='admin-muted'>{H(row.OwnerEmail)}</span></td><td><span class='admin-badge payment-{H(PaymentStageCode(row))}'>{H(PaymentStageText(row))}</span></td><td>{row.AcceptedRecipients}</td><td>{FormatMoney(row.AcceptedRecipients)}</td><td>{FormatDate(row.CreatedAt)}</td><td><a class='admin-link' href='/admin/campaigns/{row.Id}'>Кампания</a></td></tr>";

    private static string PaymentEventsRows(Mailing mailing)
    {
        var rows = new List<string>
        {
            $"<tr><td>Расчёт стоимости</td><td>Создано по последнему импорту</td><td>{FormatMoney(mailing.LastImportStats.Accepted)}</td><td>{FormatDate(mailing.CreatedAt)}</td></tr>"
        };
        var stage = PaymentStageCode(mailing);
        if (stage == "pending")
        {
            rows.Add($"<tr><td>Ожидание оплаты</td><td>{H(mailing.StatusRu)}</td><td>{FormatMoney(mailing.LastImportStats.Accepted)}</td><td>{FormatDate(mailing.CreatedAt)}</td></tr>");
        }
        else if (stage == "paid")
        {
            rows.Add($"<tr><td>Оплата подтверждена</td><td>{H(mailing.StatusRu)}</td><td>{FormatMoney(mailing.LastImportStats.Accepted)}</td><td>{FormatDate(mailing.CreatedAt)}</td></tr>");
        }
        else
        {
            rows.Add($"<tr><td>Платёж ещё не создан</td><td>{H(PaymentStageText(mailing))}</td><td>{FormatMoney(mailing.LastImportStats.Accepted)}</td><td>{FormatDate(mailing.CreatedAt)}</td></tr>");
        }

        return string.Join(string.Empty, rows);
    }

    private static string PaymentStageCode(AdminMailingSummary row)
    {
        if (row.Status == MailingStatus.Paid) return "paid";
        if (row.Status == MailingStatus.PaymentPending) return "pending";
        if (row.Status == MailingStatus.MessagePrepared) return "ready";
        return row.AcceptedRecipients > 0 && row.HasMessageDraft ? "ready" : "not_ready";
    }

    private static string PaymentStageCode(Mailing mailing)
    {
        if (mailing.Status == MailingStatus.Paid) return "paid";
        if (mailing.Status == MailingStatus.PaymentPending) return "pending";
        if (mailing.Status == MailingStatus.MessagePrepared) return "ready";
        return mailing.LastImportStats.Accepted > 0 && mailing.MessageDraft is not null ? "ready" : "not_ready";
    }

    private static string PaymentStageText(AdminMailingSummary row) => PaymentStageCode(row) switch
    {
        "paid" => "Оплачено",
        "pending" => "Ожидает оплаты",
        "ready" => "Готово к оплате",
        _ => "Не готово"
    };

    private static string PaymentStageText(Mailing mailing) => PaymentStageCode(mailing) switch
    {
        "paid" => "Оплачено",
        "pending" => "Ожидает оплаты",
        "ready" => "Готово к оплате",
        _ => "Не готово"
    };

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

    private static string FormatMoney(int acceptedRecipients) => $"{acceptedRecipients} ₽";
    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");
    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

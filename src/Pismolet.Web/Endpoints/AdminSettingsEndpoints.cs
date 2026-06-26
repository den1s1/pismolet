using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/settings", ShowSettings)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-1);
        app.MapPost("/admin/settings/billing", () => SettingsAction("billing")).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/settings/limits", () => SettingsAction("limits")).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/settings/warmup", SaveWarmupSettings).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/settings/moderation", () => SettingsAction("moderation")).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/settings/smtp", () => SettingsAction("smtp")).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/settings/smtp-test", () => SettingsAction("smtp-test")).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult ShowSettings(HttpContext http, IConfiguration configuration, IMailWarmupRuntimeSettingsRepository warmupSettingsRepository)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var action = http.Request.Query["action"].ToString();
        var actionError = http.Request.Query["error"].ToString();
        var alert = SettingsActionMessage(action, actionError);
        var alertHtml = string.IsNullOrWhiteSpace(alert) ? string.Empty : $"<p class='admin-alert'>{H(alert)}</p>";
        var smtpHost = C(configuration, "Smtp:Host", "не задан");
        var smtpPort = C(configuration, "Smtp:Port", "не задан");
        var smtpMode = C(configuration, "Smtp:SecureSocketOptions", "не задан");
        var mailProvider = C(configuration, "MailProvider", "не задан");
        var sendingQueue = C(configuration, "Sending:Queue", "не задан");
        var publicBaseUrl = C(configuration, "App:PublicBaseUrl", "не задан");
        var persistence = C(configuration, "Persistence:Provider", "не задан");
        var adminAllowlist = C(configuration, "Admin:AllowedEmails", "не задан");
        var tokenLifetime = C(configuration, "Unsubscribe:TokenLifetimeDays", "90");
        var inboundDomain = C(configuration, "InboundReplies:Domain", "reply.pismolet.ru");
        var inboundLifetime = C(configuration, "InboundReplies:TokenLifetimeDays", "180");
        var workerCount = C(configuration, "Hangfire:WorkerCount", "1");
        var fakeSender = C(configuration, "Webhooks:FakeSenderEnabled", "false");
        var warmupSettings = warmupSettingsRepository.Get();
        var warmupSource = warmupSettingsRepository.HasStoredSettings ? "админка" : "env / значения по умолчанию";

        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Настройки сервиса</h1>
                        <p class='admin-muted'>Сводка рабочих параметров Письмолёта: цены, лимиты, модерация, SMTP, системные флаги и allowlist администраторов.</p>
                    </div>
                    <a class='admin-export' href='/admin/limits'>Дневные лимиты клиентов</a>
                </div>
                {alertHtml}
                <div class='admin-stats'>
                    <div class='admin-stat'><b>{H(mailProvider)}</b><span>MailProvider</span></div>
                    <div class='admin-stat'><b>{H(sendingQueue)}</b><span>Очередь отправки</span></div>
                    <div class='admin-stat'><b>{H(persistence)}</b><span>Хранилище</span></div>
                    <div class='admin-stat'><b>{H(workerCount)}</b><span>Воркеров</span></div>
                </div>
                <div class='admin-settings-grid'>
                    <section class='admin-settings-card'>
                        <p class='eyebrow'>Биллинг</p>
                        <h2>Цены и биллинг</h2>
                        <dl>{Setting("Стоимость письма", "1 ₽ за принятое к отправке письмо")}{Setting("Оплата", "Оплачиваются только accepted-адреса")}{Setting("Публичный URL", publicBaseUrl)}</dl>
                        <form method='post' action='/admin/settings/billing'><button class='admin-button' type='submit'>Сохранить цены</button></form>
                    </section>
                    <section class='admin-settings-card'>
                        <p class='eyebrow'>Лимиты</p>
                        <h2>Лимиты клиентов</h2>
                        <dl>{Setting("Лимит по умолчанию", "1000 писем в день")}{Setting("Форма изменения", "/admin/limits")}{Setting("Unsubscribe token", $"{tokenLifetime} дней")}</dl>
                        <form method='post' action='/admin/settings/limits'><button class='admin-button' type='submit'>Сохранить лимиты</button></form>
                    </section>
                    <section class='admin-settings-card'>
                        <p class='eyebrow'>Warmup</p>
                        <h2>Прогрев и темп отправки</h2>
                        <p class='admin-muted'>Эти параметры раньше задавались через env `MailWarmup__...`. Теперь они сохраняются в runtime-настройках админки.</p>
                        <form method='post' action='/admin/settings/warmup' class='form-grid'>
                            {NumberInput("maxPerMinute", "Максимум в минуту", warmupSettings.MaxPerMinute, 0, MailWarmupRuntimeSettings.MaxLimitValue, "Например: 10")}
                            {NumberInput("maxPerHour", "Максимум в час", warmupSettings.MaxPerHour, 0, MailWarmupRuntimeSettings.MaxLimitValue, "Например: 100")}
                            {NumberInput("maxPerDay", "Максимум в день", warmupSettings.MaxPerDay, 0, MailWarmupRuntimeSettings.MaxLimitValue, "Например: 300")}
                            {NumberInput("minSecondsBetweenSends", "Минимум секунд между письмами", warmupSettings.MinSecondsBetweenSends, 0, MailWarmupRuntimeSettings.MaxDelaySeconds, "Например: 6")}
                            <button class='admin-button' type='submit'>Сохранить warmup-лимиты</button>
                        </form>
                        <dl>{Setting("Источник", warmupSource)}{Setting("Файл", warmupSettingsRepository.StoragePath)}</dl>
                        <p class='admin-muted'>После сохранения значения будут использованы при следующем старте web/worker-процесса. Для текущего запущенного процесса выполните restart сервиса.</p>
                    </section>
                    <section class='admin-settings-card'>
                        <p class='eyebrow'>Контент</p>
                        <h2>Правила модерации</h2>
                        <dl>{Setting("Премодерация", "первые и рискованные кампании")}{Setting("Очередь", "/admin/moderation")}{Setting("Режим", "гибридная проверка")}</dl>
                        <form method='post' action='/admin/settings/moderation'><button class='admin-button' type='submit'>Сохранить правила</button></form>
                    </section>
                    <section class='admin-settings-card'>
                        <p class='eyebrow'>Почта</p>
                        <h2>SMTP и отправка</h2>
                        <dl>{Setting("Провайдер", mailProvider)}{Setting("SMTP host", smtpHost)}{Setting("SMTP port", smtpPort)}{Setting("TLS", smtpMode)}{Setting("Fake sender", fakeSender)}</dl>
                        <div class='admin-actions-row'><form method='post' action='/admin/settings/smtp'><button class='admin-button' type='submit'>Сохранить SMTP</button></form><form method='post' action='/admin/settings/smtp-test'><button class='admin-button' type='submit'>Проверить SMTP</button></form></div>
                    </section>
                    <section class='admin-settings-card'>
                        <p class='eyebrow'>Система</p>
                        <h2>Системные настройки</h2>
                        <dl>{Setting("Persistence", persistence)}{Setting("Hangfire workers", workerCount)}{Setting("Inbound replies", inboundDomain)}{Setting("Reply token", $"{inboundLifetime} дней")}</dl>
                    </section>
                    <section class='admin-settings-card'>
                        <p class='eyebrow'>Доступ</p>
                        <h2>Admin allowlist</h2>
                        <dl>{Setting("Разрешённые email", adminAllowlist)}{Setting("Policy", AdminEndpoints.AdminPolicyName)}{Setting("Текущий администратор", adminEmail)}</dl>
                    </section>
                </div>
            </section>
            """;

        return AdminHtml("Админка - настройки", adminEmail, "settings", body);
    }

    private static async Task<IResult> SaveWarmupSettings(HttpContext http, IMailWarmupRuntimeSettingsRepository warmupSettingsRepository)
    {
        var form = await http.Request.ReadFormAsync();
        var settings = new MailWarmupRuntimeSettings(
            MaxPerMinute: ReadFormInt(form, "maxPerMinute", MailWarmupRuntimeSettings.Default.MaxPerMinute),
            MaxPerHour: ReadFormInt(form, "maxPerHour", MailWarmupRuntimeSettings.Default.MaxPerHour),
            MaxPerDay: ReadFormInt(form, "maxPerDay", MailWarmupRuntimeSettings.Default.MaxPerDay),
            MinSecondsBetweenSends: ReadFormInt(form, "minSecondsBetweenSends", MailWarmupRuntimeSettings.Default.MinSecondsBetweenSends));
        var result = warmupSettingsRepository.Save(settings);
        return result.Ok
            ? Results.Redirect("/admin/settings?action=warmup")
            : Results.Redirect($"/admin/settings?action=warmup-error&error={Uri.EscapeDataString(result.Error)}");
    }

    private static IResult SettingsAction(string action) =>
        Results.Redirect($"/admin/settings?action={Uri.EscapeDataString(action)}");

    private static string SettingsActionMessage(string action, string? error = null) => action switch
    {
        "billing" => "Настройки биллинга сохранены в UI. Подключение постоянного хранилища настроек будет отдельным backend-спринтом.",
        "limits" => "Настройки лимитов сохранены в UI. Индивидуальные лимиты клиентов меняются через раздел дневных лимитов.",
        "warmup" => "Warmup-лимиты сохранены в runtime-настройках. Перезапустите web/worker-процесс, чтобы применить их к текущей отправке.",
        "warmup-error" => string.IsNullOrWhiteSpace(error) ? "Не удалось сохранить warmup-лимиты." : error,
        "moderation" => "Правила модерации сохранены в UI. Боевые правила риск-скоринга останутся в коде до отдельного спринта.",
        "smtp" => "SMTP-настройки сохранены в UI. Фактические секреты остаются в env-файле сервера.",
        "smtp-test" => "Запрос на проверку SMTP принят. Боевой тест отправки будет подключён вместе с собственным SMTP-сервером.",
        _ => string.Empty
    };

    private static string Setting(string name, string value) =>
        $"<div><dt>{H(name)}</dt><dd>{H(value)}</dd></div>";

    private static string NumberInput(string name, string label, int value, int min, int max, string hint) => $"""
        <label>{H(label)}
            <input type='number' name='{H(name)}' min='{min}' max='{max}' value='{value}' required>
            <span class='field-hint'>{H(hint)}</span>
        </label>
        """;

    private static int ReadFormInt(IFormCollection form, string key, int fallback) =>
        int.TryParse(form[key].ToString(), out var parsed) ? parsed : fallback;

    private static string C(IConfiguration configuration, string key, string fallback) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key]!;

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

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

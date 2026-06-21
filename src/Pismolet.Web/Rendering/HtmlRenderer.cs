using System.Net;
using Pismolet.Web.Domain.Mail;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Rendering;

public static class HtmlRenderer
{
    public static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");

    public static string Page(string title, string body, bool authenticated = false, bool showDevTools = false)
    {
        var effectiveAuthenticated = authenticated || body.Contains("dashboard-shell", StringComparison.Ordinal);
        var accountHtml = effectiveAuthenticated ? AccountMenu(showDevTools) : PublicNav();

        return $"""
            <!doctype html>
            <html lang='ru'>
            <head>
                <meta charset='utf-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1'>
                <title>{H(title)} - Письмолёт</title>
                <link rel='icon' href='/assets/brand/favicon.svg' type='image/svg+xml'>
                <link rel='stylesheet' href='/app.css'>
            </head>
            <body>
                <header class='app-header'>
                    <div class='topbar'>
                        <a class='brand' href='/' aria-label='Письмолёт - на главную'>
                            <img class='brand-logo' src='/assets/brand/logo-horizontal.svg' alt='Письмолёт'>
                        </a>
                        {accountHtml}
                    </div>
                </header>
                <main class='page'>{body}</main>
            </body>
            </html>
            """;
    }

    public static string Error(string text) =>
        $"<section class='panel error'><h1>Ошибка</h1><p>{H(text)}</p><p><a class='btn secondary' href='/'>На главную</a></p></section>";

    public static string AccountForm(string action, string title, bool name, bool password = true) =>
        $"<section class='panel form-card'><h1>{H(title)}</h1><form method='post' action='{H(action)}'>{(name ? "<label>Название клиента<input name='displayName'></label>" : string.Empty)}<label>Email<input type='email' name='email' required></label>{(password ? "<label>Пароль<input type='password' minlength='8' name='password' required></label>" : string.Empty)}<button class='btn'>{H(title)}</button></form><p><a href='/account/resend-confirmation'>Повторить подтверждение email</a></p></section>";

    public static string Dashboard(UserAccount user)
    {
        var mailings = user.Mailings.ToList();
        var total = mailings.Count;
        var active = mailings.Count(m => m.Status is MailingStatus.Approved or MailingStatus.Sending or MailingStatus.Paused);
        var sent = mailings.Count(m => m.Status is MailingStatus.Sent);

        var rows = mailings.Count == 0
            ? "<div class='empty-state'>Пока нет рассылок. Создайте первую, чтобы проверить базу, подготовить письмо и отправить его получателям.</div>"
            : string.Join(string.Empty, mailings.Select(m =>
            {
                var (actionUrl, actionText) = DashboardAction(m);
                return $"""
                    <div class='history-row'>
                        <div>
                            <b>{H(m.Subject)}</b><br>
                            <span class='hint'>Статус: {H(m.StatusRu)}</span>
                        </div>
                        <div class='history-actions'>
                            <span class='{BadgeClass(m)}'>{H(m.StatusRu)}</span>
                            <a class='text-link' href='{H(actionUrl)}'>{H(actionText)}</a>
                        </div>
                    </div>
                    """;
            }));

        return $"""
            <section class='dashboard-shell'>
                <section class='panel quick-start'>
                    <div>
                        <p class='eyebrow'>Личный кабинет</p>
                        <h1>Запускайте email-рассылки без сложных настроек</h1>
                        <p>Загрузите адреса, подтвердите основание рассылки, подготовьте письмо и отправьте его через Письмолёт.</p>
                        <div class='actions'>
                            <a class='btn' href='/mailings/new'>Создать рассылку</a>
                            <a class='btn secondary' href='#latest-mailings'>Последние рассылки</a>
                        </div>
                    </div>
                </section>

                <section class='panel'>
                    <div class='section-head'>
                        <div>
                            <p class='eyebrow'>Аккаунт</p>
                            <h2>Состояние отправки</h2>
                        </div>
                        <span class='badge neutral'>{H(user.Profile.Status.ToString())}</span>
                    </div>
                    <div class='stats'>
                        <div class='stat'><b>{total}</b><span>Всего рассылок</span></div>
                        <div class='stat'><b>{active}</b><span>В работе</span></div>
                        <div class='stat'><b>{sent}</b><span>Отправлено</span></div>
                        <div class='stat'><b>{user.Profile.DailySendLimit}</b><span>Дневной лимит</span></div>
                    </div>
                    <p class='muted'>Общий лимит: {user.Profile.TotalSendLimit}; премодерация: {(user.Profile.PremoderationRequired ? "обязательна" : "нет")}.</p>
                </section>

                <section class='panel' id='latest-mailings'>
                    <div class='section-head'>
                        <div>
                            <p class='eyebrow'>История</p>
                            <h2>Последние рассылки</h2>
                        </div>
                        <a class='btn secondary' href='/mailings/new'>Создать рассылку</a>
                    </div>
                    <div class='history-list'>{rows}</div>
                </section>
            </section>
            """;
    }

    public static string FakeMailer(IEnumerable<FakeMail> messages) =>
        "<section class='panel'><h1>Fake mailer</h1>" +
        string.Join(string.Empty, messages.Select(x => $"<article class='mail'><b>{H(x.Subject)}</b><p>{H(x.To)}</p><a href='{H(x.Link)}'>{H(x.Link)}</a></article>")) +
        "</section>";

    private static string PublicNav() =>
        "<nav class='public-nav' aria-label='Навигация'><a href='/account/login'>Войти</a><a class='btn secondary' href='/account/register'>Регистрация</a></nav>";

    private static string AccountMenu(bool showDevTools)
    {
        var devLink = showDevTools ? "<a href='/dev/fake-mailer' role='menuitem'>Fake mailer</a>" : string.Empty;
        return $"""
            <div class='account'>
                <div class='profile-menu'>
                    <button class='profile-button' type='button' aria-haspopup='true'>Профиль ▾</button>
                    <div class='profile-dropdown' role='menu'>
                        <a href='/mailings/new' role='menuitem'>Новая рассылка</a>
                        <a href='/dashboard' role='menuitem'>История</a>
                        <a href='/admin' role='menuitem'>Админка</a>
                        {devLink}
                        <form method='post' action='/account/logout'>
                            <button type='submit'>Выйти</button>
                        </form>
                    </div>
                </div>
            </div>
            """;
    }

    private static (string Url, string Text) DashboardAction(Mailing mailing)
    {
        if (mailing.MessageDraft is null)
        {
            return ($"/mailings/{mailing.Id}", "Открыть");
        }

        return mailing.Status switch
        {
            MailingStatus.Approved or MailingStatus.Sending or MailingStatus.Sent or MailingStatus.Failed or MailingStatus.Paused => ($"/mailings/{mailing.Id}/send", "Отправка"),
            MailingStatus.Paid or MailingStatus.PendingChecks or MailingStatus.ReviewRequired or MailingStatus.Rejected => ($"/mailings/{mailing.Id}/checks", "Проверка перед отправкой"),
            _ => ($"/mailings/{mailing.Id}/payment", "Проверка и оплата")
        };
    }

    private static string BadgeClass(Mailing mailing) => mailing.Status switch
    {
        MailingStatus.Sent => "badge",
        MailingStatus.Sending or MailingStatus.Approved or MailingStatus.Paused => "badge warn",
        MailingStatus.Failed or MailingStatus.Rejected => "badge danger",
        _ => "badge neutral"
    };

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

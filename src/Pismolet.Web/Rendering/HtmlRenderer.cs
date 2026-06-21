using System.Net;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mail;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Rendering;

public static class HtmlRenderer
{
    public static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");

    public static string Page(string title, string body, bool authenticated = false, bool showDevTools = false)
    {
        var accountHtml = authenticated ? AccountMenu(showDevTools) : PublicNav();

        return $"""
            <!doctype html>
            <html lang='ru'>
            <head>
                <meta charset='utf-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1'>
                <title>{H(title)} - Письмолёт</title>
                <link rel='icon' href='/assets/brand/favicon.svg' type='image/svg+xml'>
                <link rel='stylesheet' href='/app.css'>
                <link rel='stylesheet' href='/cabinet.css'>
                <link rel='stylesheet' href='/payment.css'>
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
                        <span class='badge neutral'>{H(user.Profile.Status)}</span>
                    </div>
                    <div class='stats'>
                        <div class='stat'><b>{total}</b><span>Всего рассылок</span></div>
                        <div class='stat'><b>{active}</b><span>В работе</span></div>
                        <div class='stat'><b>{sent}</b><span>Отправлено</span></div>
                        <div class='stat'><b>{user.Profile.DailySendLimit}</b><span>Дневной лимит</span></div>
                    </div>
                </section>

                <section class='panel' id='latest-mailings'>
                    <div class='section-head'>
                        <div>
                            <p class='eyebrow'>История</p>
                            <h2>Последние рассылки</h2>
                        </div>
                        <a class='btn secondary' href='/mailings/new'>Новая рассылка</a>
                    </div>
                    <div class='history-list'>{rows}</div>
                </section>
            </section>
            """;
    }

    public static string UserProfile(UserAccount user) => $"""
        <section class='cabinet-grid'>
            <section class='panel cabinet-hero'>
                <p class='eyebrow'>Профиль</p>
                <h1>{H(user.DisplayName)}</h1>
                <p>Здесь собраны данные аккаунта, отправитель по умолчанию и лимиты, которые используются при подготовке рассылок.</p>
                <div class='actions'>
                    <a class='btn' href='/mailings/new'>Создать рассылку</a>
                    <a class='btn secondary' href='/payments'>Открыть платежи</a>
                </div>
            </section>

            <section class='panel'>
                <div class='profile-fields'>
                    <div class='profile-field'><span>Email для входа</span><b>{H(user.Email)}</b></div>
                    <div class='profile-field'><span>Телефон для связи</span><b>Не указан</b></div>
                    <div class='profile-field'><span>Отправитель по умолчанию</span><b>Письмолёт</b></div>
                    <div class='profile-field'><span>Email для пересылки ответов</span><b>{H(user.Email)}</b></div>
                    <div class='profile-field'><span>Статус аккаунта</span><b>{H(user.Profile.Status)}</b></div>
                    <div class='profile-field'><span>Дневной лимит</span><b>{user.Profile.DailySendLimit}</b></div>
                    <div class='profile-field'><span>Общий лимит</span><b>{user.Profile.TotalSendLimit}</b></div>
                    <div class='profile-field'><span>Премодерация</span><b>{(user.Profile.PremoderationRequired ? "Включена" : "Не требуется")}</b></div>
                </div>
            </section>
        </section>
        """;

    public static string Payments(UserAccount user)
    {
        var mailings = user.Mailings.ToList();
        var waiting = mailings.Where(m => m.Status is MailingStatus.Draft or MailingStatus.MessagePrepared or MailingStatus.PaymentPending).ToList();
        var paid = mailings.Where(m => m.Status is MailingStatus.Paid or MailingStatus.PendingChecks or MailingStatus.ReviewRequired or MailingStatus.Approved or MailingStatus.Sending or MailingStatus.Sent).ToList();

        return $"""
            <section class='cabinet-grid'>
                <section class='panel cabinet-hero'>
                    <p class='eyebrow'>Платежи</p>
                    <h1>Баланс и оплата рассылок</h1>
                    <p>Оплачиваются только адреса, принятые к отправке. Дубли, ошибки и ранее отписавшиеся адреса не входят в расчёт.</p>
                    <div class='actions'>
                        <a class='btn' href='/mailings/new'>Новая рассылка</a>
                        <a class='btn secondary' href='/dashboard'>История</a>
                    </div>
                </section>

                <section class='panel'>
                    <div class='section-head'><div><p class='eyebrow'>К оплате</p><h2>Ожидают оплаты</h2></div></div>
                    <div class='history-list'>{PaymentRows(waiting, "Нет рассылок, ожидающих оплаты.")}</div>
                </section>

                <section class='panel'>
                    <div class='section-head'><div><p class='eyebrow'>Оплачено</p><h2>Оплаченные рассылки</h2></div></div>
                    <div class='history-list'>{PaymentRows(paid, "Пока нет оплаченных рассылок.")}</div>
                </section>

                <section class='panel'>
                    <div class='section-head'><div><p class='eyebrow'>История</p><h2>История оплат</h2></div></div>
                    <p class='hint'>Подробная история платежей будет подключена после интеграции биллинга. Сейчас платежный статус хранится в карточке рассылки.</p>
                </section>
            </section>
            """;
    }

    public static string EmailPreview(EmailMessage message) => $"""
        <div class='card'>
            <h1>{H(message.Subject)}</h1>
            <p><strong>Кому:</strong> {H(message.Recipient.Email)}</p>
            <p><strong>Тема:</strong> {H(message.Subject)}</p>
            <pre>{H(message.PlainTextBody)}</pre>
        </div>
        """;

    public static string FakeMailer(IEnumerable<FakeMail> messages)
    {
        var list = messages.ToList();
        var rows = list.Count == 0
            ? "<div class='empty-state'>Писем пока нет.</div>"
            : string.Join(string.Empty, list.Select(message => $"""
                <div class='history-row'>
                    <div>
                        <b>{H(message.Subject)}</b><br>
                        <span class='hint'>Кому: {H(message.To)}</span>
                    </div>
                    <a class='text-link' href='{H(message.Link)}'>Открыть</a>
                </div>
                """));

        return $"""
            <section class='panel'>
                <div class='section-head'>
                    <div>
                        <p class='eyebrow'>Development</p>
                        <h1>Fake mailer</h1>
                    </div>
                    <span class='badge neutral'>{list.Count}</span>
                </div>
                <div class='history-list'>{rows}</div>
            </section>
            """;
    }

    private static string PaymentRows(IReadOnlyCollection<Mailing> mailings, string emptyText)
    {
        if (mailings.Count == 0)
        {
            return $"<div class='empty-state'>{H(emptyText)}</div>";
        }

        return string.Join(string.Empty, mailings.Select(m => $"""
            <div class='history-row'>
                <div>
                    <b>{H(m.Subject)}</b><br>
                    <span class='hint'>Статус: {H(m.StatusRu)}</span>
                </div>
                <div class='history-actions'>
                    <span class='{BadgeClass(m)}'>{H(m.StatusRu)}</span>
                    <a class='text-link' href='{H(DashboardAction(m).Url)}'>Открыть</a>
                </div>
            </div>
            """));
    }

    private static string AccountMenu(bool showDevTools)
    {
        var devLink = showDevTools ? "<a href='/dev/fake-mailer'>Fake mailer</a>" : string.Empty;
        return $"""
            <div class='profile-menu'>
                <button class='profile-button' type='button'>Профиль ▾</button>
                <div class='profile-dropdown'>
                    <a href='/mailings/new'>Новая рассылка</a>
                    <a href='/dashboard'>История</a>
                    <a href='/payments'>Платежи</a>
                    <a href='/profile'>Профиль</a>
                    <a href='/admin'>Админка</a>
                    {devLink}
                    <form method='post' action='/account/logout'><button type='submit'>Выйти</button></form>
                </div>
            </div>
            """;
    }

    private static string PublicNav() => """
        <div class='account'>
            <a class='btn secondary' href='/account/login'>Войти</a>
            <a class='btn' href='/account/register'>Регистрация</a>
        </div>
        """;

    private static (string Url, string Text) DashboardAction(Mailing mailing)
    {
        if (mailing.LastImportStats.Accepted <= 0)
        {
            return ($"/mailings/{mailing.Id}/recipients", "Продолжить");
        }

        if (mailing.Declaration is null)
        {
            return ($"/mailings/{mailing.Id}/declaration", "Подтвердить базу");
        }

        if (mailing.MessageDraft is null)
        {
            return ($"/mailings/{mailing.Id}/message", "Написать письмо");
        }

        return mailing.Status switch
        {
            MailingStatus.Paid or MailingStatus.PendingChecks or MailingStatus.ReviewRequired or MailingStatus.Approved or MailingStatus.Rejected => ($"/mailings/{mailing.Id}/checks", "Открыть проверку"),
            MailingStatus.Sending or MailingStatus.Sent or MailingStatus.Paused or MailingStatus.Failed => ($"/mailings/{mailing.Id}", "Открыть"),
            _ => ($"/mailings/{mailing.Id}/payment", "К оплате")
        };
    }

    private static string BadgeClass(Mailing mailing) => mailing.Status switch
    {
        MailingStatus.Sent or MailingStatus.Approved or MailingStatus.Paid => "badge ok",
        MailingStatus.Rejected or MailingStatus.Failed => "badge danger",
        MailingStatus.PaymentPending or MailingStatus.PendingChecks or MailingStatus.ReviewRequired => "badge warn",
        _ => "badge neutral"
    };

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

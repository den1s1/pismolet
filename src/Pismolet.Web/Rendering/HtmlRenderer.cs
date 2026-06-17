using Pismolet.Web.Domain.Mail;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Rendering;

public static class HtmlRenderer
{
    public static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");

    public static string Page(string title, string body) =>
        $"<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>{title} — Письмолёт</title><link rel='stylesheet' href='/app.css'></head><body><header><a class='brand' href='/'>✉️ Письмолёт</a><nav><a href='/dashboard'>ЛК</a><a href='/dev/fake-mailer'>Fake mailer</a></nav></header><main>{body}</main></body></html>";

    public static string Error(string text) =>
        $"<section class='card error'><h1>Ошибка</h1><p>{text}</p><p><a href='/'>На главную</a></p></section>";

    public static string AccountForm(string action, string title, bool name, bool password = true) =>
        $"<section class='card form-card'><h1>{title}</h1><form method='post' action='{action}'>{(name ? "<label>Название клиента<input name='displayName'></label>" : string.Empty)}<label>Email<input type='email' name='email' required></label>{(password ? "<label>Пароль<input type='password' minlength='8' name='password' required></label>" : string.Empty)}<button class='button'>{title}</button></form><p><a href='/account/resend-confirmation'>Повторить подтверждение email</a></p></section>";

    public static string Dashboard(UserAccount user)
    {
        var rows = string.Join(string.Empty, user.Mailings.Select(m =>
        {
            var actionUrl = m.MessageDraft is null ? $"/mailings/{m.Id}" : $"/mailings/{m.Id}/payment";
            var actionText = m.MessageDraft is null ? "Открыть" : "Проверка и оплата";
            return $"<tr><td>{m.Subject}</td><td><span class='badge'>{m.StatusRu}</span></td><td><a href='{actionUrl}'>{actionText}</a></td></tr>";
        }));
        return $"<section class='card'><div class='topline'><div><p class='eyebrow'>Личный кабинет</p><h1>Ваши рассылки</h1></div><form method='post' action='/account/logout'><button>Выйти</button></form></div><p class='muted'>Статус клиента: {user.Profile.Status}. Дневной лимит: {user.Profile.DailySendLimit}; общий лимит: {user.Profile.TotalSendLimit}; премодерация: {(user.Profile.PremoderationRequired ? "обязательна" : "нет")}.</p><a class='create-card' href='/mailings/new'>+ Создать рассылку</a><table><thead><tr><th>Рассылка</th><th>Статус</th><th>Действие</th></tr></thead><tbody>{rows}</tbody></table></section>";
    }

    public static string FakeMailer(IEnumerable<FakeMail> messages) =>
        "<section class='card'><h1>Fake mailer</h1>" +
        string.Join(string.Empty, messages.Select(x => $"<article class='mail'><b>{x.Subject}</b><p>{x.To}</p><a href='{x.Link}'>{x.Link}</a></article>")) +
        "</section>";
}

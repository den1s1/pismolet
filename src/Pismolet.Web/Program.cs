using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/account/login";
});
builder.Services.AddAuthorization();
builder.Services.AddSingleton<DemoStore>();

var app = builder.Build();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Html(Page("Письмолёт", "<section class='hero'><h1>Рассылка клиентам без скрытых копий, таблиц и риска заблокировать рабочую почту.</h1><p>Простой старт: регистрация, подтверждение email и личный кабинет.</p><p><a class='button' href='/account/register'>Зарегистрироваться</a> <a class='button secondary' href='/account/login'>Войти</a></p></section>")));

app.MapGet("/account/register", () => Html(Page("Регистрация", Form("/account/register", "Создать аккаунт", true))));
app.MapPost("/account/register", async (HttpContext http, DemoStore store) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim().ToLowerInvariant();
    var password = form["password"].ToString();
    var displayName = form["displayName"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(email) || password.Length < 8) return Html(Page("Ошибка", Error("Укажите email и пароль от 8 символов.")));
    var result = store.Register(email, password, string.IsNullOrWhiteSpace(displayName) ? email : displayName, http);
    if (!result.Ok) return Html(Page("Ошибка", Error(result.Error)));
    return Html(Page("Подтверждение", $"<section class='card'><h1>Аккаунт создан</h1><p>Dev/fake mailer подготовил ссылку подтверждения.</p><p><a class='button' href='{result.ConfirmLink}'>Подтвердить email</a></p><p><a href='/dev/fake-mailer'>Открыть fake mailer</a></p></section>"));
});

app.MapGet("/account/confirm-email", (string token, DemoStore store, HttpContext http) =>
{
    var ok = store.ConfirmEmail(token, http);
    return Html(Page("Email", ok ? "<section class='card'><h1>Email подтверждён</h1><p><a class='button' href='/account/login'>Войти</a></p></section>" : Error("Ссылка подтверждения недействительна.")));
});

app.MapGet("/account/resend-confirmation", () => Html(Page("Повторить подтверждение", Form("/account/resend-confirmation", "Отправить ссылку", false, false))));
app.MapPost("/account/resend-confirmation", async (HttpContext http, DemoStore store) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim().ToLowerInvariant();
    var link = store.ResendConfirmation(email);
    return Html(Page("Повторить подтверждение", $"<section class='card'><h1>Готово</h1><p>{(link is null ? "Если пользователь существует, письмо будет подготовлено." : $"<a class='button' href='{link}'>Dev-ссылка</a>")}</p></section>"));
});

app.MapGet("/account/login", () => Html(Page("Вход", Form("/account/login", "Войти", false))));
app.MapPost("/account/login", async (HttpContext http, DemoStore store) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim().ToLowerInvariant();
    var password = form["password"].ToString();
    var user = store.Authenticate(email, password, http);
    if (user is null) return Html(Page("Ошибка входа", Error("Неверный email/пароль или email ещё не подтверждён.")));
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Email), new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Name, user.DisplayName) };
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Redirect("/dashboard");
});

app.MapPost("/account/logout", async (HttpContext http, DemoStore store) =>
{
    var email = http.User.FindFirstValue(ClaimTypes.Email);
    if (email is not null) store.Audit(email, "logout", http, "{}");
    await http.SignOutAsync();
    return Results.Redirect("/");
}).RequireAuthorization();

app.MapGet("/dashboard", (HttpContext http, DemoStore store) =>
{
    var email = http.User.FindFirstValue(ClaimTypes.Email)!;
    var user = store.Users[email];
    var rows = string.Join("", user.Mailings.Select(m => $"<tr><td>{m.Subject}</td><td><span class='badge'>{m.StatusRu}</span></td></tr>"));
    var body = $"<section class='card'><div class='topline'><div><p class='eyebrow'>Личный кабинет</p><h1>Ваши рассылки</h1></div><form method='post' action='/account/logout'><button>Выйти</button></form></div><p class='muted'>Статус клиента: {user.Profile.Status}. Дневной лимит: {user.Profile.DailySendLimit}; общий лимит: {user.Profile.TotalSendLimit}; премодерация: {(user.Profile.PremoderationRequired ? "обязательна" : "нет")}.</p><a class='create-card' href='#'>+ Создать рассылку</a><table><thead><tr><th>Рассылка</th><th>Статус</th></tr></thead><tbody>{rows}</tbody></table></section>";
    return Html(Page("Личный кабинет", body));
}).RequireAuthorization();

app.MapGet("/dev/fake-mailer", (DemoStore store) => Html(Page("Fake mailer", "<section class='card'><h1>Fake mailer</h1>" + string.Join("", store.Outbox.Reverse().Select(x => $"<article class='mail'><b>{x.Subject}</b><p>{x.To}</p><a href='{x.Link}'>{x.Link}</a></article>")) + "</section>")));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");
static string Error(string text) => $"<section class='card error'><h1>Ошибка</h1><p>{text}</p><p><a href='/'>На главную</a></p></section>";
static string Form(string action, string title, bool name, bool password = true) => $"<section class='card form-card'><h1>{title}</h1><form method='post' action='{action}'>{(name ? "<label>Название клиента<input name='displayName'></label>" : "")}<label>Email<input type='email' name='email' required></label>{(password ? "<label>Пароль<input type='password' name='password' minlength='8' required></label>" : "")}<button class='button'>{title}</button></form><p><a href='/account/resend-confirmation'>Повторить подтверждение email</a></p></section>";
static string Page(string title, string body) => $"<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>{title} — Письмолёт</title><link rel='stylesheet' href='/app.css'></head><body><header><a class='brand' href='/'>✉️ Письмолёт</a><nav><a href='/dashboard'>ЛК</a><a href='/dev/fake-mailer'>Fake mailer</a></nav></header><main>{body}</main></body></html>";

sealed class DemoStore
{
    public ConcurrentDictionary<string, DemoUser> Users { get; } = new();
    public List<FakeMail> Outbox { get; } = [];
    public List<AuditRecord> AuditLog { get; } = [];
    public RegisterResult Register(string email, string password, string displayName, HttpContext http)
    {
        if (Users.ContainsKey(email)) return new(false, "Пользователь уже существует.", null);
        var token = Guid.NewGuid().ToString("N");
        var user = new DemoUser(email, password, displayName, token, false, new ClientProfile("active", 1000, 10000, true), [new Mailing("Первая рассылка", "Черновик")]);
        Users[email] = user;
        Audit(email, "registration", http, "{}");
        var link = "/account/confirm-email?token=" + token;
        Outbox.Add(new FakeMail(email, "Подтверждение email", link));
        return new(true, "", link);
    }
    public bool ConfirmEmail(string token, HttpContext http)
    {
        var pair = Users.FirstOrDefault(x => x.Value.ConfirmationToken == token);
        if (pair.Value is null) return false;
        Users[pair.Key] = pair.Value with { EmailConfirmed = true };
        Audit(pair.Key, "email_confirmed", http, "{}");
        return true;
    }
    public string? ResendConfirmation(string email)
    {
        if (!Users.TryGetValue(email, out var user)) return null;
        var link = "/account/confirm-email?token=" + user.ConfirmationToken;
        Outbox.Add(new FakeMail(email, "Повторное подтверждение email", link));
        return link;
    }
    public DemoUser? Authenticate(string email, string password, HttpContext http)
    {
        if (!Users.TryGetValue(email, out var user) || user.Password != password || !user.EmailConfirmed) return null;
        Audit(email, "login", http, "{}");
        return user;
    }
    public void Audit(string email, string type, HttpContext http, string context) => AuditLog.Add(new AuditRecord(DateTimeOffset.UtcNow, email, type, http.Connection.RemoteIpAddress?.ToString() ?? "", http.Request.Headers.UserAgent.ToString(), context));
}
record DemoUser(string Email, string Password, string DisplayName, string ConfirmationToken, bool EmailConfirmed, ClientProfile Profile, List<Mailing> Mailings);
record ClientProfile(string Status, int DailySendLimit, int TotalSendLimit, bool PremoderationRequired);
record Mailing(string Subject, string StatusRu);
record FakeMail(string To, string Subject, string Link);
record AuditRecord(DateTimeOffset CreatedAt, string User, string EventType, string Ip, string UserAgent, string Context);

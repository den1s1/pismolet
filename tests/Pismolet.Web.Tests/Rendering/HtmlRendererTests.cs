using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Tests.Rendering;

public sealed class HtmlRendererTests
{
    [Fact]
    public void Public_page_does_not_show_authenticated_menu()
    {
        var html = HtmlRenderer.Page("Вход", HtmlRenderer.AccountForm("/account/login", "Войти", name: false));

        Assert.Contains("/assets/brand/logo-horizontal.svg", html);
        Assert.Contains("/account/login", html);
        Assert.Contains("/account/register", html);
        Assert.DoesNotContain("Профиль ▾", html);
        Assert.DoesNotContain("/account/logout", html);
        Assert.DoesNotContain("/dev/fake-mailer", html);
    }

    [Fact]
    public void Authenticated_page_shows_profile_menu_without_dev_link()
    {
        var html = HtmlRenderer.Page("Личный кабинет", "<p>body</p>", authenticated: true);

        Assert.Contains("Профиль ▾", html);
        Assert.Contains("/mailings/new", html);
        Assert.Contains("/dashboard", html);
        Assert.Contains("/payments", html);
        Assert.Contains("/profile", html);
        Assert.Contains("/admin", html);
        Assert.Contains("/account/logout", html);
        Assert.DoesNotContain("/dev/fake-mailer", html);
    }

    [Fact]
    public void Dashboard_page_contains_first_sprint_ui_elements()
    {
        var user = new UserAccount(
            Email: "owner@example.test",
            PasswordHash: "dev:password",
            DisplayName: "Owner",
            ConfirmationToken: "token",
            EmailConfirmed: true,
            Profile: ClientProfile.NewClientDefault(),
            Mailings: [Mailing.Draft("Тестовая рассылка")]);

        var html = HtmlRenderer.Page("Личный кабинет", HtmlRenderer.Dashboard(user), authenticated: true);

        Assert.Contains("/assets/brand/logo-horizontal.svg", html);
        Assert.Contains("Создать рассылку", html);
        Assert.Contains("История рассылок", html);
        Assert.Contains("Тестовая рассылка", html);
        Assert.Contains("Черновик", html);
    }

    [Fact]
    public void Profile_page_contains_second_sprint_profile_fields()
    {
        var user = TestUser();
        var html = HtmlRenderer.Page("Профиль", HtmlRenderer.UserProfile(user), authenticated: true);

        Assert.Contains("Email для входа", html);
        Assert.Contains("owner@example.test", html);
        Assert.Contains("Телефон для связи", html);
        Assert.Contains("Отправитель по умолчанию", html);
        Assert.Contains("Email для пересылки ответов", html);
        Assert.Contains("Дневной лимит", html);
        Assert.Contains("Открыть платежи", html);
    }

    [Fact]
    public void Payments_page_contains_payment_sections_and_mailing_statuses()
    {
        var pending = Mailing.Draft("Ждёт оплаты").WithMessageDraft(MailingMessageDraft.Create("Письмолёт", "Ждёт оплаты", "Текст", MessageType.Transactional, DateTimeOffset.UtcNow));
        var paid = Mailing.Draft("Оплачена").WithStatus(MailingStatus.Paid);
        var user = TestUser() with { Mailings = [pending, paid] };

        var html = HtmlRenderer.Page("Платежи", HtmlRenderer.Payments(user), authenticated: true);

        Assert.Contains("Ожидают оплаты", html);
        Assert.Contains("Оплаченные рассылки", html);
        Assert.Contains("История оплат", html);
        Assert.Contains("Ждёт оплаты", html);
        Assert.Contains("Оплачена", html);
        Assert.Contains("Оплачено", html);
    }

    private static UserAccount TestUser() => new(
        Email: "owner@example.test",
        PasswordHash: "dev:password",
        DisplayName: "Owner",
        ConfirmationToken: "token",
        EmailConfirmed: true,
        Profile: ClientProfile.NewClientDefault(),
        Mailings: []);
}

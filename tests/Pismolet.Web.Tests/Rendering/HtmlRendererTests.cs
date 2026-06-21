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
        Assert.DoesNotContain("Профиль", html);
        Assert.DoesNotContain("/account/logout", html);
        Assert.DoesNotContain("/dev/fake-mailer", html);
    }

    [Fact]
    public void Authenticated_page_shows_profile_menu_without_dev_link()
    {
        var html = HtmlRenderer.Page("Личный кабинет", "<p>body</p>", authenticated: true);

        Assert.Contains("Профиль", html);
        Assert.Contains("/mailings/new", html);
        Assert.Contains("/dashboard", html);
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
        Assert.Contains("Последние рассылки", html);
        Assert.Contains("Тестовая рассылка", html);
        Assert.Contains("Черновик", html);
    }
}

using Pismolet.Web.Rendering;

namespace Pismolet.Web.Tests;

public sealed class HtmlRendererLayoutTests
{
    [Fact]
    public void Public_page_does_not_render_authenticated_menu_or_dev_links()
    {
        var html = HtmlRenderer.Page(
            "Вход",
            HtmlRenderer.AccountForm("/account/login", "Войти", name: false));

        Assert.Contains("brand-logo", html);
        Assert.Contains("/account/login", html);
        Assert.Contains("/account/register", html);
        Assert.DoesNotContain("Профиль", html);
        Assert.DoesNotContain("/account/logout", html);
        Assert.DoesNotContain("/admin", html);
        Assert.DoesNotContain("/dev/fake-mailer", html);
    }

    [Fact]
    public void Authenticated_page_renders_account_menu_without_dev_link_by_default()
    {
        var html = HtmlRenderer.Page(
            "Личный кабинет",
            "<section><h1>Последние рассылки</h1><a href='/mailings/new'>Создать рассылку</a><span>Статус</span></section>",
            authenticated: true);

        Assert.Contains("brand-logo", html);
        Assert.Contains("Профиль", html);
        Assert.Contains("/mailings/new", html);
        Assert.Contains("Создать рассылку", html);
        Assert.Contains("Последние рассылки", html);
        Assert.Contains("Статус", html);
        Assert.DoesNotContain("/dev/fake-mailer", html);
    }

    [Fact]
    public void Authenticated_page_renders_dev_link_only_when_requested()
    {
        var html = HtmlRenderer.Page("Dev", "<p>ok</p>", authenticated: true, showDevTools: true);

        Assert.Contains("/dev/fake-mailer", html);
    }
}

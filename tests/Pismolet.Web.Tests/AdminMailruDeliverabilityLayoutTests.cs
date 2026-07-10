using Microsoft.AspNetCore.Http;
using Pismolet.Web.Endpoints;

namespace Pismolet.Web.Tests;

public sealed class AdminMailruDeliverabilityLayoutTests
{
    [Fact]
    public void DeliverabilityPage_IsWrappedInAdminShellWithActiveNavigation()
    {
        const string html = "<!doctype html><html><body><main class='page'><section class='admin-panel'><a href='/admin/deliverability/mailru?days=30'>30 дней</a></section></main></body></html>";

        var result = AdminMailruDeliverabilityMenuMiddleware.AddDeliverabilityLayout(
            html,
            new PathString("/admin/deliverability/mailru"),
            "admin@example.test");

        Assert.Contains("class='admin-shell'", result);
        Assert.Contains("class='admin-sidebar'", result);
        Assert.Contains("class='admin-content'", result);
        Assert.Contains("<strong>admin@example.test</strong>", result);
        Assert.Contains("class='admin-nav-link active' href='/admin/deliverability/mailru'", result);
        Assert.Contains("<section class='admin-panel'>", result);
    }

    [Fact]
    public void DeliverabilityPage_DoesNotGetWrappedTwice()
    {
        const string html = "<!doctype html><html><body><main class='page'><section class='admin-shell'><div class='admin-content'>content</div></section></main></body></html>";

        var result = AdminMailruDeliverabilityMenuMiddleware.AddDeliverabilityLayout(
            html,
            new PathString("/admin/deliverability/mailru"),
            "admin@example.test");

        Assert.Equal(html, result);
    }

    [Fact]
    public void AdminEmail_IsHtmlEncodedInInjectedShell()
    {
        const string html = "<!doctype html><html><body><main class='page'><section class='admin-panel'>content</section></main></body></html>";

        var result = AdminMailruDeliverabilityMenuMiddleware.AddDeliverabilityLayout(
            html,
            new PathString("/admin/deliverability/mailru"),
            "admin+<test>@example.test");

        Assert.Contains("admin+&lt;test&gt;@example.test", result);
        Assert.DoesNotContain("admin+<test>@example.test", result);
    }
}

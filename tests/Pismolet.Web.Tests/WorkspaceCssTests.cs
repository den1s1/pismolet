namespace Pismolet.Web.Tests;

public sealed class WorkspaceCssTests
{
    [Fact]
    public void Cabinet_css_defines_stable_workspace_width_without_limiting_admin()
    {
        var css = ReadWebAsset("src/Pismolet.Web/wwwroot/cabinet.css");

        Assert.Contains(".cabinet-grid,\n.dashboard-shell,\n.wizard-shell", css);
        Assert.Contains("max-width: 960px;", css);
        Assert.Contains(".wizard-shell:has(.message-wizard-grid)", css);
        Assert.Contains("max-width: 1040px;", css);
        Assert.Contains(".admin-shell", css);
        Assert.Contains("width: calc(100vw - 48px);", css);
        Assert.Contains("max-width: none;", css);
    }

    [Fact]
    public void Admin_width_rules_live_outside_payment_css()
    {
        var paymentCss = ReadWebAsset("src/Pismolet.Web/wwwroot/payment.css");
        var cabinetCss = ReadWebAsset("src/Pismolet.Web/wwwroot/cabinet.css");

        Assert.DoesNotContain(".admin-shell", paymentCss);
        Assert.Contains(".admin-content .form-card", cabinetCss);
        Assert.Contains("@media (max-width: 1120px)", cabinetCss);
    }

    [Fact]
    public void Cabinet_tables_have_explicit_scroll_wrapper()
    {
        var css = ReadWebAsset("src/Pismolet.Web/wwwroot/cabinet.css");

        Assert.Contains(".table-wrap", css);
        Assert.Contains("overflow-x: auto;", css);
    }

    private static string ReadWebAsset(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pismolet.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, relativePath);
        Assert.True(File.Exists(path), $"Asset not found: {path}");
        return File.ReadAllText(path);
    }
}

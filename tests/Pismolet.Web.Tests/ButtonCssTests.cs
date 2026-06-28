namespace Pismolet.Web.Tests;

public sealed class ButtonCssTests
{
    [Fact]
    public void App_css_uses_explicit_button_variants_without_primary_style_on_plain_button()
    {
        var css = ReadWebAsset("src/Pismolet.Web/wwwroot/app.css");

        Assert.DoesNotContain(".btn,\n.button,\nbutton", css);
        Assert.DoesNotContain("\n.secondary {", css);
        Assert.DoesNotContain("\n.secondary,", css);
        Assert.Contains(".btn,\n.button {", css);
        Assert.Contains("button {\n  font: inherit;\n}", css);
        Assert.Contains(".btn.secondary", css);
        Assert.Contains(".button.secondary", css);
        Assert.Contains(".btn.tertiary", css);
        Assert.Contains(".button.tertiary", css);
        Assert.Contains(".btn.compact", css);
        Assert.Contains(".button.compact", css);
        Assert.Contains(".control-link", css);
    }

    [Fact]
    public void Cabinet_css_compacts_inline_wizard_actions_without_markup_rewrite()
    {
        var css = ReadWebAsset("src/Pismolet.Web/wwwroot/cabinet.css");

        Assert.Contains(".inline-base-confirm form.row .btn", css);
        Assert.Contains(".inline-base-confirm form.row .button", css);
        Assert.Contains(".table-wrap form .btn.ghost", css);
        Assert.Contains("min-height: 34px", css);
    }

    [Fact]
    public void Cabinet_css_keeps_rich_editor_toolbar_compact_and_text_area_visible()
    {
        var css = ReadWebAsset("src/Pismolet.Web/wwwroot/cabinet.css");

        Assert.Contains(".rich-toolbar {\n  display: grid;", css);
        Assert.Contains("grid-template-columns: 38px 38px 118px auto minmax(220px, 1fr);", css);
        Assert.Contains(".rich-color-control {", css);
        Assert.Contains(".rich-link-control {", css);
        Assert.Contains(".rich-editable {\n  display: block;", css);
        Assert.Contains("min-height: 260px", css);
    }

    [Fact]
    public void Payment_css_does_not_stretch_primary_payment_button_on_desktop()
    {
        var css = ReadWebAsset("src/Pismolet.Web/wwwroot/payment.css");

        Assert.Contains(".full-pay-button{width:auto", css);
        Assert.Contains(".full-pay-button{width:100%!important", css);
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

using Pismolet.Web.Rendering;

namespace Pismolet.Web.Tests;

public sealed class HtmlRendererTests
{
    [Fact]
    public void Account_registration_form_uses_checkbox_component_for_required_legal_consents()
    {
        var html = HtmlRenderer.AccountForm("/account/register", "Регистрация", name: true, registrationConsents: true);

        Assert.Contains("class='form-consents checkbox-list'", html);
        Assert.Contains("<label class='checkbox-row'><input class='checkbox-control' type='checkbox' name='acceptOffer' value='true' required><span class='checkbox-text'>", html);
        Assert.Contains("<label class='checkbox-row'><input class='checkbox-control' type='checkbox' name='acceptPrivacy' value='true' required><span class='checkbox-text'>", html);
        Assert.Contains("</span></label>", html);
        Assert.Contains("/legal/offer?returnUrl=/account/register", html);
        Assert.Contains("/legal/rules?returnUrl=/account/register", html);
        Assert.Contains("/legal/client-consent?returnUrl=/account/register", html);
        Assert.Contains("/legal/privacy?returnUrl=/account/register", html);
    }

    [Fact]
    public void Page_loads_common_checkbox_styles_before_payment_styles()
    {
        var html = HtmlRenderer.Page("Проверка", "<p>body</p>");

        var checkboxIndex = html.IndexOf("href='/checkbox.css'", StringComparison.Ordinal);
        var paymentIndex = html.IndexOf("href='/payment.css'", StringComparison.Ordinal);
        Assert.True(checkboxIndex > 0, "checkbox.css must be linked globally.");
        Assert.True(paymentIndex > checkboxIndex, "payment.css must not be the source of common checkbox styles.");
    }
}

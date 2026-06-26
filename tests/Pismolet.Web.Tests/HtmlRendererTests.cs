using Pismolet.Web.Rendering;

namespace Pismolet.Web.Tests;

public sealed class HtmlRendererTests
{
    [Fact]
    public void Account_registration_form_keeps_required_legal_consents()
    {
        var html = HtmlRenderer.AccountForm("/account/register", "Регистрация", name: true, registrationConsents: true);

        Assert.Contains("class='form-consents'", html);
        Assert.Contains("type='checkbox' name='acceptOffer' value='true' required", html);
        Assert.Contains("type='checkbox' name='acceptPrivacy' value='true' required", html);
        Assert.Contains("/legal/offer?returnUrl=/account/register", html);
        Assert.Contains("/legal/rules?returnUrl=/account/register", html);
        Assert.Contains("/legal/client-consent?returnUrl=/account/register", html);
        Assert.Contains("/legal/privacy?returnUrl=/account/register", html);
    }
}

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Pismolet.Web.Domain.Legal;

namespace Pismolet.Web.Tests;

public sealed class RegistrationLegalDocumentTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly WebApplicationFactory<global::Program> factory;

    public RegistrationLegalDocumentTests(WebApplicationFactory<global::Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Theory]
    [InlineData("/legal/offer", "Правила и оферта сервиса Письмолёт", "document_key: <code>offer_and_rules</code>")]
    [InlineData("/legal/privacy", "Политика обработки персональных данных", "document_key: <code>client_personal_data_consent</code>")]
    public async Task LegalDocumentsArePublic(string path, string title, string documentKey)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(title, html);
        Assert.Contains(documentKey, html);
    }

    [Fact]
    public async Task RegistrationFormContainsLegalDocumentLinksInSameTab()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/account/register");

        Assert.Contains("href='/legal/offer'", html);
        Assert.Contains("правила и оферту сервиса", html);
        Assert.Contains("href='/legal/privacy'", html);
        Assert.Contains("политике обработки персональных данных", html);
        Assert.DoesNotContain("target='_blank'", html);
        Assert.DoesNotContain("rel='noopener'", html);
    }

    [Fact]
    public async Task RegistrationWithoutLegalCheckboxesIsRejected()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["displayName"] = "Иван Иванов",
            ["phone"] = "+79990000000",
            ["email"] = "ivan@example.test",
            ["password"] = "password123"
        });

        var response = await client.PostAsync("/account/register", form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Подтвердите обязательные условия регистрации.", html);
    }

    [Fact]
    public void RegistrationLegalEvidenceSnapshotsReferenceCurrentDocuments()
    {
        Assert.Contains("document_key=offer_and_rules", LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText);
        Assert.Contains($"document_version={LegalEvidenceTextSnapshots.CurrentVersion}", LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText);
        Assert.Contains("document_url=/legal/offer", LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText);

        Assert.Contains("document_key=client_personal_data_consent", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
        Assert.Contains($"document_version={LegalEvidenceTextSnapshots.CurrentVersion}", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
        Assert.Contains("document_url=/legal/privacy", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
    }
}

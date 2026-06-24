using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mail;
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
    public async Task SuccessfulRegistrationRecordsLegalEvidenceEvents()
    {
        var email = $"legal-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var form = CreateRegistrationForm(email);

        var response = await client.PostAsync("/account/register", form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Аккаунт создан", html);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ILegalEvidenceRepository>();
        var events = repository.ListEventsForClient(email, 10);

        AssertRegistrationEvent(
            events,
            LegalEventTypes.OfferAndRulesAccepted,
            LegalDocumentKeys.OfferAndRules,
            LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText,
            "offerAccepted");

        AssertRegistrationEvent(
            events,
            LegalEventTypes.ClientPersonalDataConsentAccepted,
            LegalDocumentKeys.ClientPersonalDataConsent,
            LegalEvidenceTextSnapshots.ClientPersonalDataConsentText,
            "personalDataConsentAccepted");
    }

    [Fact]
    public async Task LoginIsBlockedUntilEmailIsConfirmed()
    {
        var email = $"confirm-{Guid.NewGuid():N}@example.test";
        const string password = "password123";
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using (var registrationForm = CreateRegistrationForm(email, password))
        {
            var registration = await client.PostAsync("/account/register", registrationForm);
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        }

        using (var loginForm = CreateLoginForm(email, password))
        {
            var blockedLogin = await client.PostAsync("/account/login", loginForm);
            var blockedHtml = await blockedLogin.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, blockedLogin.StatusCode);
            Assert.Contains("Неверный email/пароль или email ещё не подтверждён.", blockedHtml);
        }

        using var scope = factory.Services.CreateScope();
        var mailer = scope.ServiceProvider.GetRequiredService<IFakeMailer>();
        var confirmationMail = Assert.Single(mailer.GetOutbox(), mail => mail.To.Equals(email, StringComparison.OrdinalIgnoreCase));
        var confirmationPath = ToPathAndQuery(confirmationMail.Link);

        var confirmation = await client.GetAsync(confirmationPath);
        var confirmationHtml = await confirmation.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Contains("Email подтверждён", confirmationHtml);

        using (var loginForm = CreateLoginForm(email, password))
        {
            var allowedLogin = await client.PostAsync("/account/login", loginForm);

            Assert.Equal(HttpStatusCode.Redirect, allowedLogin.StatusCode);
            Assert.Equal("/dashboard", allowedLogin.Headers.Location?.OriginalString);
        }
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

    private static FormUrlEncodedContent CreateRegistrationForm(string email, string password = "password123") => new(new Dictionary<string, string>
    {
        ["displayName"] = "Иван Иванов",
        ["phone"] = "+79990000000",
        ["email"] = email,
        ["password"] = password,
        ["acceptOffer"] = "true",
        ["acceptPrivacy"] = "true"
    });

    private static FormUrlEncodedContent CreateLoginForm(string email, string password) => new(new Dictionary<string, string>
    {
        ["email"] = email,
        ["password"] = password
    });

    private static void AssertRegistrationEvent(
        IReadOnlyCollection<LegalEvidenceEvent> events,
        string eventType,
        string documentKey,
        string snapshot,
        string metadataFlag)
    {
        var item = Assert.Single(events, x => x.EventType == eventType);

        Assert.Equal(documentKey, item.DocumentKey);
        Assert.Equal(LegalEvidenceTextSnapshots.CurrentVersion, item.DocumentVersion);
        Assert.False(string.IsNullOrWhiteSpace(item.TextHash));
        Assert.Equal(snapshot, item.EventTextSnapshot);
        Assert.Equal(LegalEventResults.Accepted, item.Result);
        Assert.Equal("/account/register", item.Route);
        Assert.Contains("source", item.MetadataJson);
        Assert.Contains("registration_form", item.MetadataJson);
        Assert.Contains(metadataFlag, item.MetadataJson);
    }

    private static string ToPathAndQuery(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery;
        }

        return link;
    }
}

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class RegistrationLegalDocumentTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly WebApplicationFactory<global::Program> factory;

    public RegistrationLegalDocumentTests(WebApplicationFactory<global::Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task LegalIndexIsPublic()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/legal");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Юридические документы Письмолёта", html);
        Assert.Contains("href='/legal/offer'", html);
        Assert.Contains("href='/legal/privacy'", html);
        Assert.Contains("href='/legal/client-consent'", html);
        Assert.Contains("href='/legal/rules'", html);
        Assert.Contains("href='/legal/data-processing'", html);
        Assert.Contains("href='/legal/base-lawfulness'", html);
        Assert.Contains("href='/legal/advertising-consent'", html);
    }

    [Theory]
    [InlineData("/legal/offer", "Пользовательское соглашение и оферта сервиса Письмолёт", "document_key: <code>offer_and_rules</code>")]
    [InlineData("/legal/privacy", "Политика обработки персональных данных", "document_key: <code>privacy_policy</code>")]
    [InlineData("/legal/client-consent", "Согласие клиента на обработку персональных данных", "document_key: <code>client_personal_data_consent</code>")]
    [InlineData("/legal/rules", "Правила рассылок Письмолёта", "document_key: <code>mailing_rules</code>")]
    [InlineData("/legal/data-processing", "Поручение на обработку данных адресатов", "document_key: <code>recipient_data_processing_instruction</code>")]
    [InlineData("/legal/base-lawfulness", "Декларация законности базы", "document_key: <code>base_lawfulness_declaration</code>")]
    [InlineData("/legal/advertising-consent", "Подтверждение рекламного согласия адресатов", "document_key: <code>advertising_consent_declaration</code>")]
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
    public async Task OfferContainsFullLegalSections()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var html = await client.GetStringAsync("/legal/offer");

        Assert.Contains("Редакция:</strong> 2026-06-24-v1", html);
        Assert.Contains("Акцепт соглашения", html);
        Assert.Contains("База адресатов", html);
        Assert.Contains("Отписка через Письмолёт", html);
        Assert.Contains("Ограничения ответственности", html);
        Assert.Contains("Связанные документы", html);
    }

    [Fact]
    public async Task RulesContainFullLegalSections()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var html = await client.GetStringAsync("/legal/rules");

        Assert.Contains("Редакция:</strong> 2026-06-24-v1", html);
        Assert.Contains("Запрещённые базы", html);
        Assert.Contains("Подтверждения клиента", html);
        Assert.Contains("Отписка через Письмолёт", html);
        Assert.Contains("Модерация и отказ в отправке", html);
    }

    [Fact]
    public async Task PrivacyContainsFullLegalSections()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var html = await client.GetStringAsync("/legal/privacy");

        Assert.Contains("Редакция:</strong> 2026-06-24-v1", html);
        Assert.Contains("Оператор", html);
        Assert.Contains("Какие данные обрабатываются", html);
        Assert.Contains("Отписка через Письмолёт и жалобы", html);
        Assert.Contains("Хранение и удаление", html);
        Assert.Contains("Права субъектов данных", html);
    }

    [Fact]
    public async Task ClientConsentContainsFullLegalSections()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var html = await client.GetStringAsync("/legal/client-consent");

        Assert.Contains("Редакция:</strong> 2026-06-24-v1", html);
        Assert.Contains("Кто даёт согласие", html);
        Assert.Contains("Какие данные клиента обрабатываются", html);
        Assert.Contains("Юридически значимые подтверждения", html);
        Assert.Contains("href='/legal/privacy'", html);
        Assert.Contains("Отзыв согласия", html);
    }

    [Fact]
    public async Task DataProcessingContainsFullLegalSections()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var html = await client.GetStringAsync("/legal/data-processing");

        Assert.Contains("Редакция:</strong> 2026-06-24-v1", html);
        Assert.Contains("Кто отвечает за законность базы", html);
        Assert.Contains("Что поручается Письмолёту", html);
        Assert.Contains("Ограничение состава данных", html);
        Assert.Contains("Отписка через Письмолёт", html);
        Assert.Contains("Хранение и удаление", html);
    }

    [Fact]
    public async Task BaseLawfulnessContainsFullLegalSections()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var html = await client.GetStringAsync("/legal/base-lawfulness");

        Assert.Contains("Редакция:</strong> 2026-06-24-v1", html);
        Assert.Contains("Что подтверждает клиент", html);
        Assert.Contains("Что нельзя загружать", html);
        Assert.Contains("Рекламные письма", html);
        Assert.Contains("Запрос подтверждений", html);
        Assert.Contains("Последствия недостоверной декларации", html);
    }

    [Fact]
    public async Task AdvertisingConsentContainsFullLegalSections()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var html = await client.GetStringAsync("/legal/advertising-consent");

        Assert.Contains("Редакция:</strong> 2026-06-24-v1", html);
        Assert.Contains("Когда требуется подтверждение", html);
        Assert.Contains("Что подтверждает клиент", html);
        Assert.Contains("Что не делает Письмолёт", html);
        Assert.Contains("Запрос доказательств", html);
        Assert.Contains("href='/legal/base-lawfulness'", html);
    }

    [Fact]
    public async Task RegistrationFormContainsLegalDocumentLinksInSameTab()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/account/register");

        Assert.Contains("href='/legal/offer'", html);
        Assert.Contains("href='/legal/rules'", html);
        Assert.Contains("оферту", html);
        Assert.Contains("правила рассылок", html);
        Assert.Contains("href='/legal/client-consent'", html);
        Assert.Contains("согласие на обработку моих персональных данных", html);
        Assert.Contains("href='/legal/privacy'", html);
        Assert.Contains("сведения профиля", html);
        Assert.Contains("данные об оплатах", html);
        Assert.Contains("политика обработки персональных данных", html);
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
            ["password"] = "TestPassword123!"
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
    public void RegistrationLegalEvidenceSnapshotsReferenceCurrentDocuments()
    {
        Assert.Contains("document_key=offer_and_rules", LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText);
        Assert.Contains($"document_version={LegalEvidenceTextSnapshots.CurrentVersion}", LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText);
        Assert.Contains("document_url=/legal/offer", LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText);

        Assert.Contains("document_key=client_personal_data_consent", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
        Assert.Contains($"document_version={LegalEvidenceTextSnapshots.CurrentVersion}", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
        Assert.Contains("document_url=/legal/client-consent", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
        Assert.Contains("policy_url=/legal/privacy", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
        Assert.Contains("сведения профиля", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);
        Assert.Contains("данные об оплатах", LegalEvidenceTextSnapshots.ClientPersonalDataConsentText);

        Assert.Contains("document_key=recipient_data_processing_instruction", LegalEvidenceTextSnapshots.RecipientDataProcessingInstructionText);
        Assert.Contains($"document_version={LegalEvidenceTextSnapshots.CurrentVersion}", LegalEvidenceTextSnapshots.RecipientDataProcessingInstructionText);
        Assert.Contains("document_url=/legal/data-processing", LegalEvidenceTextSnapshots.RecipientDataProcessingInstructionText);

        Assert.Contains("document_key=base_lawfulness_declaration", BaseDeclarationText.Text);
        Assert.Contains($"document_version={BaseDeclarationText.CurrentVersion}", BaseDeclarationText.Text);
        Assert.Contains("document_url=/legal/base-lawfulness", BaseDeclarationText.Text);

        Assert.Contains("document_key=advertising_consent_declaration", LegalEvidenceTextSnapshots.AdvertisingConsentText);
        Assert.Contains($"document_version={LegalEvidenceTextSnapshots.CurrentVersion}", LegalEvidenceTextSnapshots.AdvertisingConsentText);
        Assert.Contains("document_url=/legal/advertising-consent", LegalEvidenceTextSnapshots.AdvertisingConsentText);
    }

    private static FormUrlEncodedContent CreateRegistrationForm(string email, string password = "TestPassword123!") => new(new Dictionary<string, string>
    {
        ["displayName"] = "Иван Иванов",
        ["phone"] = "+79990000000",
        ["email"] = email,
        ["password"] = password,
        ["acceptOffer"] = "true",
        ["acceptPrivacy"] = "true"
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
}

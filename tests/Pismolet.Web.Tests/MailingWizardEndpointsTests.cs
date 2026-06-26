using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class MailingWizardEndpointsTests
{
    private const string OwnerEmail = "wizard-owner@example.test";

    [Fact]
    public async Task Authenticated_user_can_start_new_mailing_from_addresses_step()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, OwnerEmail);

        var response = await client.GetAsync("/mailings/new");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.StartsWith("/mailings/", location, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/recipients", location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authenticated_user_can_open_address_wizard_step()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Wizard campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/recipients");

        Assert.Contains("1. Добавьте список адресов", html);
        Assert.Contains("Не используйте купленные или чужие базы", html);
        Assert.Contains("/legal/anti-spam", html);
        Assert.Contains("name='manualAddresses'", html);
        Assert.Contains("dropzone", html);
        Assert.Contains("address-upload-block", html);
        Assert.Contains("Адреса добавлены, дальше", html);
        Assert.Contains("3. Расчёт и оплата", html);
        Assert.Contains("4. Готово", html);
        Assert.DoesNotContain(">Черновик<", html);
    }

    [Fact]
    public async Task Manual_address_import_preserves_accepted_recipients_and_shows_integrated_base_confirmation()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Manual import campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("first@example.test\nwrong-email\nFIRST@example.test"), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("1. Адреса загружены", html);
        Assert.Contains("Сводка импорта", html);
        Assert.Contains("Управление адресами", html);
        Assert.Contains("address-summary-block", html);
        Assert.Contains("address-base-block", html);
        Assert.Contains("address-list-block", html);
        Assert.DoesNotContain("Адреса проверены", html);
        Assert.Contains("Принято к отправке", html);
        Assert.Contains("<b>1</b><span>Принято к отправке</span>", html);
        Assert.Contains("<b>2</b><span>Дублей и ошибок</span>", html);
        Assert.Contains("Ранее отписались", html);
        Assert.DoesNotContain("Что исключено", html);
        Assert.DoesNotContain("Исключённых адресов нет", html);
        Assert.Contains("Подтвердите базу", html);
        Assert.DoesNotContain("Источник и подтверждения фиксируются", html);
        Assert.Contains("Источник базы", html);
        Assert.Contains("Тип письма", html);
        Assert.Contains("compact-base-fields", html);
        Assert.Contains("advertisingConsentBlock", html);
        Assert.Contains("подтверждаю правомерность использования базы", html);
        Assert.Contains("/legal/data-processing", html);
        Assert.Contains("поручаю техническую обработку email-адресов", html);
        Assert.Contains("подтверждаю наличие рекламного согласия адресатов", html);
        Assert.Contains("/legal/advertising-consent", html);
        Assert.Contains("Декларация законности базы", html);
        Assert.Contains("/legal/base-lawfulness", html);
        Assert.Contains("Перейти к письму", html);
        Assert.Contains($"/mailings/{mailingId}/declaration", html);
        Assert.DoesNotContain("Текст декларации", html);
        Assert.DoesNotContain("Полный текст вынесен", html);
        Assert.DoesNotContain("Открыть декларацию", html);
        Assert.DoesNotContain("style=", html);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(3, mailing.LastImportStats.TotalRows);
        Assert.Equal(1, mailing.LastImportStats.Accepted);
        Assert.Equal(1, mailing.LastImportStats.Duplicates);
        Assert.Equal(1, mailing.LastImportStats.Invalid);
        Assert.Equal(0, mailing.LastImportStats.GloballySuppressed);
        Assert.Equal(3, mailing.Recipients.Count);
        var acceptedRecipient = Assert.Single(mailing.Recipients.Where(x => x.Status == RecipientStatus.Accepted));
        Assert.Equal("first@example.test", acceptedRecipient.Email);
        Assert.Contains(mailing.Recipients, x => x.Status == RecipientStatus.Invalid && x.SourceEmail == "wrong-email");
        Assert.Contains(mailing.Recipients, x => x.Status == RecipientStatus.Duplicate && x.Email == "first@example.test");
        Assert.NotNull(mailing.LastImportBatch);
        Assert.Equal(3, mailing.LastImportBatch.TotalRows);
        Assert.Equal(1, mailing.LastImportBatch.Accepted);
        Assert.Equal(1, mailing.LastImportBatch.Duplicates);
        Assert.Equal(1, mailing.LastImportBatch.Invalid);
        Assert.Equal(2, mailing.LastImportBatch.Issues.Count);
    }

    [Fact]
    public async Task Manual_address_import_with_base_confirmation_redirects_to_message_and_saves_declaration()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Direct declaration import campaign");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, OwnerEmail);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("reader@example.test"), "manualAddresses" },
            { new StringContent("Customers"), "baseSource" },
            { new StringContent("on"), "baseLegality" },
            { new StringContent("Transactional"), "messageType" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/message", response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.NotNull(mailing.Declaration);
        Assert.Equal(BaseSource.Customers, mailing.Declaration.BaseSource);
        Assert.True(mailing.Declaration.IsBaseLegalityConfirmed);
        Assert.False(mailing.Declaration.IsAdvertisingConsentConfirmed);
    }

    [Fact]
    public async Task Address_management_edit_keeps_bad_rows_and_import_stats()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Address management regression campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        using var importContent = new MultipartFormDataContent
        {
            { new StringContent("first@example.test\nwrong-email\nsecond@example.test\nFIRST@example.test"), "manualAddresses" }
        };
        var importResponse = await client.PostAsync($"/mailings/{mailingId}/recipients", importContent);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);

        using var addForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "added@example.test"
        });
        var addResponse = await client.PostAsync($"/mailings/{mailingId}/recipients/add", addForm);
        var htmlAfterAdd = await addResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        Assert.Contains("wrong-email", htmlAfterAdd);
        Assert.Contains("FIRST@example.test", htmlAfterAdd);
        Assert.Contains("<b>5</b><span>Строк в файле</span>", htmlAfterAdd);
        Assert.Contains("<b>3</b><span>Принято к отправке</span>", htmlAfterAdd);
        Assert.Contains("<b>2</b><span>Дублей и ошибок</span>", htmlAfterAdd);

        Mailing? mailingAfterAdd;
        using (var scope = factory.Services.CreateScope())
        {
            var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
            mailingAfterAdd = mailings.GetForOwner(mailingId, OwnerEmail);
        }

        Assert.NotNull(mailingAfterAdd);
        Assert.Equal(5, mailingAfterAdd.LastImportStats.TotalRows);
        Assert.Equal(3, mailingAfterAdd.LastImportStats.Accepted);
        Assert.Equal(1, mailingAfterAdd.LastImportStats.Invalid);
        Assert.Equal(1, mailingAfterAdd.LastImportStats.Duplicates);
        Assert.Equal(5, mailingAfterAdd.Recipients.Count);
        Assert.Contains(mailingAfterAdd.Recipients, x => x.Status == RecipientStatus.Invalid && x.SourceEmail == "wrong-email");
        Assert.Contains(mailingAfterAdd.Recipients, x => x.Status == RecipientStatus.Duplicate && x.SourceEmail == "FIRST@example.test");

        var secondRecipient = Assert.Single(mailingAfterAdd.Recipients.Where(x => x.Email == "second@example.test" && x.Status == RecipientStatus.Accepted));
        using var removeForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = secondRecipient.Email,
            ["rowNumber"] = secondRecipient.RowNumber.ToString()
        });
        var removeResponse = await client.PostAsync($"/mailings/{mailingId}/recipients/remove", removeForm);
        var htmlAfterRemove = await removeResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.Contains("wrong-email", htmlAfterRemove);
        Assert.Contains("FIRST@example.test", htmlAfterRemove);
        Assert.Contains("<b>4</b><span>Строк в файле</span>", htmlAfterRemove);
        Assert.Contains("<b>2</b><span>Принято к отправке</span>", htmlAfterRemove);
        Assert.Contains("<b>2</b><span>Дублей и ошибок</span>", htmlAfterRemove);

        using (var scope = factory.Services.CreateScope())
        {
            var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
            var mailingAfterRemove = mailings.GetForOwner(mailingId, OwnerEmail);
            Assert.NotNull(mailingAfterRemove);
            Assert.Equal(4, mailingAfterRemove.LastImportStats.TotalRows);
            Assert.Equal(2, mailingAfterRemove.LastImportStats.Accepted);
            Assert.Equal(1, mailingAfterRemove.LastImportStats.Invalid);
            Assert.Equal(1, mailingAfterRemove.LastImportStats.Duplicates);
            Assert.Equal(4, mailingAfterRemove.Recipients.Count);
            Assert.Contains(mailingAfterRemove.Recipients, x => x.Status == RecipientStatus.Invalid && x.SourceEmail == "wrong-email");
            Assert.Contains(mailingAfterRemove.Recipients, x => x.Status == RecipientStatus.Duplicate && x.SourceEmail == "FIRST@example.test");
        }
    }

    [Fact]
    public async Task Manual_address_import_rejects_oversized_input_before_import()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Oversized manual import campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(new string('a', 1024 * 1024 + 1)), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ручная вставка слишком большая", html);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Empty(mailing.Recipients);
    }

    [Fact]
    public async Task Manual_address_import_rejects_too_many_rows_before_import()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Too many rows manual import campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        var manualAddresses = string.Join('\n', Enumerable.Range(1, 1001).Select(index => $"person{index}@example.test"));
        using var content = new MultipartFormDataContent
        {
            { new StringContent(manualAddresses), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ручная вставка содержит больше 1000 строк", html);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Empty(mailing.Recipients);
    }

    [Fact]
    public async Task Authenticated_user_can_open_message_wizard_step_after_declaration()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Message wizard campaign");
        using var client = CreateAuthenticatedClient(factory, OwnerEmail);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);

        var html = await client.GetStringAsync($"/mailings/{mailingId}/message");

        Assert.Contains("2. Напишите письмо", html);
        Assert.Contains("Предпросмотр", html);
        Assert.Contains("Обычный текст", html);
        Assert.Contains("HTML", html);
        Assert.Contains("name='senderName'", html);
        Assert.Contains("name='plainBody'", html);
        Assert.Contains("name='htmlBody'", html);
        Assert.Contains("Политика запрещённого контента", html);
        Assert.Contains($"/legal/prohibited-content?returnUrl=/mailings/{mailingId}/message", html);
        Assert.Contains("Не отправляйте мошенничество", html);
        Assert.Contains("Письмолёт автоматически добавит", html);
        Assert.Contains("Служебный блок письма", html);
        Assert.Contains($"/legal/service-email-footer?returnUrl=/mailings/{mailingId}/message", html);
        Assert.Contains("Проверить и оплатить", html);
        Assert.DoesNotContain("<div class='mail-preview-body'>", html);
        Assert.DoesNotContain("name='messageType'", html);
        Assert.DoesNotContain("Тип письма", html);
        Assert.DoesNotContain("Перейти к проверке и оплате", html);
    }

    [Fact]
    public async Task Saving_message_preserves_draft_and_redirects_to_payment()
    {
        using var factory = CreateAuthorizedFactory();
        SeedUser(factory, OwnerEmail, "Wizard Owner");
        var mailingId = SeedMailing(factory, OwnerEmail, "Save message campaign");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, OwnerEmail);
        await ImportAcceptedAddress(client, mailingId);
        await ConfirmBaseDeclaration(client, mailingId);
        using var messageForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Библиотека №5",
            ["subject"] = "Приглашаем на встречу",
            ["body"] = "Здравствуйте!\n\nБудем рады видеть вас."
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/message", messageForm);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/payment", response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing?.MessageDraft);
        Assert.Equal("Библиотека №5", mailing.MessageDraft.SenderName);
        Assert.Equal("Приглашаем на встречу", mailing.MessageDraft.Subject);
        Assert.Equal("Здравствуйте!\n\nБудем рады видеть вас.", mailing.MessageDraft.Body);
        Assert.Equal(MessageType.Transactional, mailing.MessageDraft.MessageType);
    }

    private static async Task ImportAcceptedAddress(HttpClient client, Guid mailingId)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("reader@example.test"), "manualAddresses" }
        };

        var response = await client.PostAsync($"/mailings/{mailingId}/recipients", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ConfirmBaseDeclaration(HttpClient client, Guid mailingId)
    {
        using var declarationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["baseSource"] = "Customers",
            ["baseLegality"] = "on",
            ["messageType"] = "Transactional"
        });

        var response = await client.PostAsync($"/mailings/{mailingId}/declaration", declarationForm);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected declaration response: {(int)response.StatusCode}");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });

    private static WebApplicationFactory<Program> CreateAuthorizedFactory() =>
        CreateFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            });
        });

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeaderName, email);
        return client;
    }

    private static void SeedUser(WebApplicationFactory<Program> factory, string email, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var result = accounts.Register(new RegisterUserCommand(email, "PassForTests2026!", displayName, "+79990000000"), Request());
        Assert.True(result.Ok, result.Error);
    }

    private static Guid SeedMailing(WebApplicationFactory<Program> factory, string ownerEmail, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var result = mailings.CreateDraft(new CreateMailingCommand(ownerEmail, subject), Request());
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Mailing);
        return result.Mailing.Id;
    }

    private static RequestMetadata Request() => new("127.0.0.1", "mailing-wizard-endpoint-tests");

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string EmailHeaderName = "X-Test-Email";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Request.Headers[EmailHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(email))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, email),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, email)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

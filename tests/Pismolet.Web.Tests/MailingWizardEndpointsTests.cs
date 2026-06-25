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
        Assert.Contains("name='manualAddresses'", html);
        Assert.Contains("dropzone", html);
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
        Assert.Contains("Адреса проверены", html);
        Assert.Contains("Принято к отправке", html);
        Assert.Contains("<b>1</b><span>Принято к отправке</span>", html);
        Assert.Contains("<b>2</b><span>Дублей и ошибок</span>", html);
        Assert.Contains("Ранее отписались", html);
        Assert.Contains("Подтвердите базу", html);
        Assert.Contains("Источник базы", html);
        Assert.Contains("Тип письма", html);
        Assert.Contains("compact-base-fields", html);
        Assert.Contains("advertisingConsentBlock", html);
        Assert.Contains("подтверждаю наличие рекламного согласия адресатов", html);
        Assert.Contains("Декларация законности базы", html);
        Assert.Contains("/legal/base-lawfulness", html);
        Assert.Contains("Перейти к письму", html);
        Assert.Contains($"/mailings/{mailingId}/declaration", html);
        Assert.DoesNotContain("Текст декларации", html);
        Assert.DoesNotContain("Полный текст вынесен", html);
        Assert.DoesNotContain("Открыть декларацию", html);

        using var scope = factory.Services.CreateScope();
        var mailings = scope.ServiceProvider.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(mailingId, OwnerEmail);
        Assert.NotNull(mailing);
        Assert.Equal(3, mailing.LastImportStats.TotalRows);
        Assert.Equal(1, mailing.LastImportStats.Accepted);
        Assert.Equal(1, mailing.LastImportStats.Duplicates);
        Assert.Equal(1, mailing.LastImportStats.Invalid);
        Assert.Equal(0, mailing.LastImportStats.GloballySuppressed);
        var recipient = Assert.Single(mailing.Recipients);
        Assert.Equal(RecipientStatus.Accepted, recipient.Status);
        Assert.NotNull(mailing.LastImportBatch);
        Assert.Equal(3, mailing.LastImportBatch.TotalRows);
        Assert.Equal(1, mailing.LastImportBatch.Accepted);
        Assert.Equal(1, mailing.LastImportBatch.Duplicates);
        Assert.Equal(1, mailing.LastImportBatch.Invalid);
        Assert.Equal(2, mailing.LastImportBatch.Issues.Count);
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
        Assert.Contains("Превью письма", html);
        Assert.Contains("name='senderName'", html);
        Assert.Contains("name='body'", html);
        Assert.Contains("Письмолёт автоматически добавит", html);
        Assert.Contains("Служебный идентификатор рассылки", html);
        Assert.Contains("Проверить и оплатить", html);
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

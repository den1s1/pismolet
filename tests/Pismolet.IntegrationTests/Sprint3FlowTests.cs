using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Mail;
using Xunit;

namespace Pismolet.IntegrationTests;

public sealed class Sprint3FlowTests
{
    [Fact]
    public async Task FullFlow_import_declaration_message_succeeds()
    {
        using var factory = CreateFactory();
        var client = CreateClient(factory);
        var email = UniqueEmail();

        await RegisterAndLoginAsync(factory, client, email);
        var mailingId = await CreateMailingAndImportCsvAsync(client);

        var declaration = await client.PostAsync($"/mailings/{mailingId}/declaration", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["baseSource"] = "Customers",
            ["messageType"] = "Advertising",
            ["baseLegality"] = "on",
            ["advertisingConsent"] = "on"
        }));

        Assert.Equal(HttpStatusCode.Redirect, declaration.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/message", declaration.Headers.Location?.ToString());

        var message = await client.PostAsync($"/mailings/{mailingId}/message", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["senderName"] = "Письмолёт",
            ["subject"] = "Новости",
            ["body"] = "Текст письма"
        }));

        Assert.Equal(HttpStatusCode.Redirect, message.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/payment", message.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogger>().GetRecords();
        Assert.Contains(audit, x => x.EventType == "mailing_declaration_confirmed"
            && x.Context.Contains("\"mailingId\"", StringComparison.Ordinal)
            && x.Context.Contains("\"importBatchId\"", StringComparison.Ordinal)
            && x.Context.Contains("\"declarationVersion\"", StringComparison.Ordinal)
            && x.Context.Contains("\"baseSource\"", StringComparison.Ordinal)
            && x.Context.Contains("\"intendedMessageType\"", StringComparison.Ordinal));
        Assert.Contains(audit, x => x.EventType == "mailing_message_saved");
    }

    [Fact]
    public async Task Declaration_without_required_checkbox_returns_error()
    {
        using var factory = CreateFactory();
        var client = CreateClient(factory);

        await RegisterAndLoginAsync(factory, client, UniqueEmail());
        var mailingId = await CreateMailingAndImportCsvAsync(client);

        var response = await client.PostAsync($"/mailings/{mailingId}/declaration", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["baseSource"] = "Customers",
            ["messageType"] = "Transactional"
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Подтвердите базу", body);
        Assert.Contains("/legal/base-lawfulness", body);
        Assert.Contains("/legal/data-processing", body);
    }

    [Fact]
    public async Task Message_without_declaration_redirects_to_recipients_step()
    {
        using var factory = CreateFactory();
        var client = CreateClient(factory);

        await RegisterAndLoginAsync(factory, client, UniqueEmail());
        var mailingId = await CreateMailingAndImportCsvAsync(client);

        var response = await client.GetAsync($"/mailings/{mailingId}/message");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/mailings/{mailingId}/recipients", response.Headers.Location?.ToString());
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory"
                });
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task RegisterAndLoginAsync(WebApplicationFactory<Program> factory, HttpClient client, string email)
    {
        const string password = "PassForTests2026!";
        var register = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
            ["displayName"] = "Тестовый Пользователь",
            ["phone"] = "+79990000000",
            ["acceptOffer"] = "true",
            ["acceptPrivacy"] = "true"
        }));

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var fakeMailer = factory.Services.GetRequiredService<IFakeMailer>();
        var confirmLink = fakeMailer
            .GetOutbox()
            .FirstOrDefault(message => message.To.Equals(email, StringComparison.OrdinalIgnoreCase)
                && message.Link.Contains("/account/confirm-email", StringComparison.Ordinal))
            ?.Link;
        Assert.False(string.IsNullOrWhiteSpace(confirmLink), "В тестовом outbox не найдена ссылка подтверждения email.");

        var confirm = await client.GetAsync(confirmLink);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var login = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password
        }));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/dashboard", login.Headers.Location?.ToString());
    }

    private static async Task<Guid> CreateMailingAndImportCsvAsync(HttpClient client)
    {
        var create = await client.PostAsync("/mailings", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["subject"] = "Новости"
        }));

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var location = create.Headers.Location?.ToString() ?? string.Empty;
        var match = Regex.Match(location, @"/mailings/(?<id>[0-9a-fA-F-]+)/recipients");
        Assert.True(match.Success, $"Не удалось получить id рассылки из redirect: {location}");
        var mailingId = Guid.Parse(match.Groups["id"].Value);

        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("email\nlead@example.com\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        multipart.Add(file, "file", "leads.csv");

        var import = await client.PostAsync($"/mailings/{mailingId}/recipients", multipart);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var body = await import.Content.ReadAsStringAsync();
        Assert.Contains("Принято к отправке", body);
        Assert.Contains("<b>1</b><span>Принято к отправке</span>", body);

        return mailingId;
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";
}

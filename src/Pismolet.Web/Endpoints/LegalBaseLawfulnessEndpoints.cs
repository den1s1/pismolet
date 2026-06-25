using System.Net;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class LegalBaseLawfulnessEndpoints
{
    public static IEndpointRouteBuilder MapLegalBaseLawfulnessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/legal/base-lawfulness", ShowBaseLawfulness);
        return app;
    }

    private static IResult ShowBaseLawfulness(HttpContext http)
    {
        var returnUrl = http.Request.Query["returnUrl"].ToString();
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal) || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            returnUrl = "/legal";
        }

        var body = $"""
            <section class='panel legal-document'>
                <p><a href='{H(returnUrl)}'>← Вернуться к рассылке</a></p>
                <p class='eyebrow'>Юридический документ</p>
                <h1>Декларация законности базы</h1>
                <p class='hint'>document_key: <code>{H(LegalDocumentKeys.BaseLawfulnessDeclaration)}</code>, version: <code>{H(BaseDeclarationText.CurrentVersion)}</code></p>

                <p><strong>Редакция:</strong> 2026-06-24-v1.</p>
                <p><strong>Статус:</strong> декларация клиента о законности загруженной базы email-адресов и допустимости отправки писем.</p>

                <h2>1. Что подтверждает клиент</h2>
                <p>Клиент подтверждает, что имеет законное основание для обработки загружаемых email-адресов и отправки писем этим адресатам.</p>
                <p>Клиент подтверждает, что использует собственную законную базу email-адресов и имеет основание для обработки этих адресов и отправки писем адресатам.</p>
                <p>Письмолёт не проверяет законность базы вместо клиента и не подтверждает наличие согласий адресатов. Ответственность за происхождение базы, основание обработки и допустимость письма несёт клиент.</p>

                <h2>2. Что нельзя загружать</h2>
                <p>Нельзя использовать купленные, украденные, спарсенные, чужие или иным образом незаконно полученные базы.</p>
                <p>Нельзя использовать адреса из открытых каталогов, форумов, чатов, социальных сетей, чужих CRM, реестров или иных источников без подтверждённого законного основания.</p>
                <p>В MVP по получателям принимаются только email-адреса. Не загружайте ФИО получателей, телефоны, должности, названия организаций, заметки из CRM и другие поля персонализации.</p>

                <h2>3. Рекламные письма</h2>
                <p>Если письмо является рекламным или клиент не уверен в его статусе, клиент обязан подтвердить наличие предварительного согласия адресатов на получение рекламных сообщений.</p>
                <p>Декларация законности базы не заменяет отдельное подтверждение рекламного согласия для рекламных и сомнительных писем.</p>

                <h2>4. Запрос подтверждений</h2>
                <p>При жалобе получателя, подозрении на нарушение или запросе уполномоченных органов Письмолёт может попросить клиента объяснить источник базы и предоставить подтверждения: форму заявки, договор, регистрацию, согласие, историю взаимодействия или иной документ.</p>
                <p>Клиент понимает, что обязан предоставить подтверждения по запросу Письмолёта, адресата или уполномоченных органов.</p>

                <h2>5. Последствия недостоверной декларации</h2>
                <p>Если подтверждения не предоставлены или недостаточны, рассылка и новые отправки могут быть ограничены, остановлены или отправлены на дополнительную модерацию.</p>
                <p>При признаках купленной, чужой, спарсенной или незаконной базы сервис может отказать в отправке, снизить лимиты или ограничить аккаунт.</p>

                <div class='actions'>
                    <a class='button' href='{H(returnUrl)}'>Всё понятно</a>
                </div>
            </section>
            """;

        return HtmlRenderer.Html(HtmlRenderer.Page("Декларация законности базы", body));
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

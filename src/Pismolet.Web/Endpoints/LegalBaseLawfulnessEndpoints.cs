using System.Net;
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
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/', StringComparison.Ordinal) || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            returnUrl = "/legal";
        }

        var body = $"""
            <section class='panel legal-document'>
                <p><a href='{H(returnUrl)}'>← Вернуться к рассылке</a></p>
                <p class='eyebrow'>Юридический документ</p>
                <h1>Декларация законности базы</h1>
                <p class='hint'>document_key: <code>base_lawfulness</code>, version: <code>2026-06-24-v1</code></p>

                <h2>Что подтверждает клиент</h2>
                <p>Клиент подтверждает, что использует собственную законную базу email-адресов и имеет основание для обработки этих адресов и отправки писем адресатам.</p>
                <p>Письмолёт не проверяет законность базы вместо клиента и не подтверждает наличие согласий адресатов. Ответственность за происхождение базы, основание обработки и допустимость письма несёт клиент.</p>

                <h2>Что нельзя загружать</h2>
                <p>Нельзя использовать купленные, чужие, украденные, спарсенные, скачанные из открытых источников или иным образом незаконно полученные базы.</p>
                <p>В MVP по получателям принимаются только email-адреса. Не загружайте ФИО получателей, телефоны, должности, названия организаций, заметки из CRM и другие поля персонализации.</p>

                <h2>Запрос подтверждений</h2>
                <p>При жалобе получателя, подозрении на нарушение или запросе уполномоченных органов Письмолёт может попросить клиента объяснить источник базы и предоставить подтверждения: форму заявки, договор, регистрацию, согласие, историю взаимодействия или иной документ.</p>
                <p>Если подтверждения не предоставлены или недостаточны, рассылка и новые отправки могут быть ограничены.</p>

                <div class='actions'>
                    <a class='button' href='{H(returnUrl)}'>Всё понятно</a>
                </div>
            </section>
            """;

        return HtmlRenderer.Html(HtmlRenderer.Page("Декларация законности базы", body));
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

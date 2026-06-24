using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class LegalDocumentEndpoints
{
    public static IEndpointRouteBuilder MapLegalDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/legal/offer", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Правила и оферта",
            LegalDocumentPage(
                "Правила и оферта сервиса Письмолёт",
                LegalDocumentKeys.OfferAndRules,
                LegalEvidenceTextSnapshots.CurrentVersion,
                OfferBody()))));

        app.MapGet("/legal/privacy", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Политика обработки персональных данных",
            LegalDocumentPage(
                "Политика обработки персональных данных",
                LegalDocumentKeys.ClientPersonalDataConsent,
                LegalEvidenceTextSnapshots.CurrentVersion,
                PrivacyBody()))));

        return app;
    }

    private static string LegalDocumentPage(string title, string documentKey, string version, string body) => $$"""
        <section class='panel legal-document'>
            <p><a href='/account/register'>← К регистрации</a></p>
            <p class='eyebrow'>Юридический документ</p>
            <h1>{{title}}</h1>
            <p class='hint'>document_key: <code>{{documentKey}}</code>, version: <code>{{version}}</code></p>
            {{body}}
        </section>
        """;

    private static string OfferBody() => """
        <h2>1. Назначение сервиса</h2>
        <p>Письмолёт — сервис для подготовки и отправки простых email-рассылок по поручению клиента.</p>

        <h2>2. Ответственность клиента</h2>
        <p>Клиент самостоятельно определяет содержание рассылки, адресатов и законность использования своей адресной базы.</p>
        <p>Клиент подтверждает, что не использует купленные, чужие или незаконно полученные базы адресов.</p>

        <h2>3. Роль сервиса</h2>
        <p>Письмолёт технически обрабатывает адреса, проверяет формат, исключает дубли, учитывает отписки через Письмолёт и отправляет письма по поручению клиента.</p>

        <h2>4. Подтверждения</h2>
        <p>При регистрации, загрузке базы, подтверждении источника адресов и запуске рассылки сервис фиксирует дату, время, IP, user-agent, версию документа и текст подтверждения.</p>
        """;

    private static string PrivacyBody() => """
        <h2>1. Данные клиента</h2>
        <p>При регистрации Письмолёт обрабатывает ФИО, email, телефон, технические данные запроса и сведения о действиях клиента в сервисе.</p>

        <h2>2. Данные получателей</h2>
        <p>При создании рассылок сервис может обрабатывать email-адреса получателей, загруженные клиентом, а также технические события отправки, отписок через Письмолёт, жалоб и ошибок доставки.</p>

        <h2>3. Цели обработки</h2>
        <p>Данные используются для регистрации аккаунта, подтверждения email, связи с клиентом, оказания услуги рассылки, антиспам-контроля и ведения юридически значимой истории действий.</p>

        <h2>4. Согласие</h2>
        <p>Регистрируясь в сервисе, клиент даёт согласие на обработку данных, указанных при регистрации, в объёме, необходимом для работы Письмолёта.</p>
        """;
}

using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin", () => HtmlRenderer.Html(HtmlRenderer.Page("Админ-зона", "<section class='card'><h1>Админ-зона</h1><p>Заглушка внутренних инструментов MVP.</p><p>Роли будут добавлены позже.</p><p><a href='/dashboard'>Вернуться в ЛК</a></p></section>"))).RequireAuthorization();
        return app;
    }
}

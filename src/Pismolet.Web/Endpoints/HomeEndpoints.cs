using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class HomeEndpoints
{
    public static IEndpointRouteBuilder MapHomeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Письмолёт",
            "<section class='hero'><h1>Рассылка клиентам без скрытых копий, таблиц и риска заблокировать рабочую почту.</h1><p>Простой старт: регистрация, подтверждение email и личный кабинет.</p><p><a class='button' href='/account/register'>Зарегистрироваться</a> <a class='button secondary' href='/account/login'>Войти</a></p></section>")));

        return app;
    }
}

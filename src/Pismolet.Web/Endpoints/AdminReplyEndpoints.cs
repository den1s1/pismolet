namespace Pismolet.Web.Endpoints;

public static class AdminReplyEndpoints
{
    public static IEndpointRouteBuilder MapAdminReplyEndpoints(this IEndpointRouteBuilder app)
    {
        // Маршрут /admin/replies уже зарегистрирован в AdminSprint10Endpoints.
        // Этот extension оставлен временно для совместимости с Program.cs и будет удалён после объединения admin endpoints.
        return app;
    }
}

using Pismolet.Web.Application.Mail;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/fake-mailer", (IFakeMailer fakeMailer) => HtmlRenderer.Html(HtmlRenderer.Page(
            "Fake mailer",
            HtmlRenderer.FakeMailer(fakeMailer.GetOutbox()))));

        return app;
    }
}

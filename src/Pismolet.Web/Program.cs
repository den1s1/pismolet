using Microsoft.AspNetCore.Authentication.Cookies;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
    });

builder.Services.AddAuthorization();
builder.Services.AddPismoletWebServices();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHomeEndpoints();
app.MapAccountEndpoints();
app.MapDashboardEndpoints();
app.MapDevEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

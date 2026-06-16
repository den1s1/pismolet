using Microsoft.AspNetCore.Authentication.Cookies;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.DependencyInjection;
using Pismolet.Web.Infrastructure.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();
builder.Services.AddPismoletWebServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.Services.GetRequiredService<DevSeedDataInitializer>().Seed();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHomeEndpoints();
app.MapAccountEndpoints();
app.MapDashboardEndpoints();
app.MapAdminEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDevEndpoints();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program
{
}

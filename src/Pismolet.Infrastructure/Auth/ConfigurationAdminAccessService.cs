using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Common;

namespace Pismolet.Web.Infrastructure.Auth;

public sealed class ConfigurationAdminAccessService(IConfiguration configuration) : IAdminAccessService
{
    public bool IsAdminEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return ReadAdminEmails(configuration).Contains(email.Trim().ToLowerInvariant());
    }

    private static IReadOnlySet<string> ReadAdminEmails(IConfiguration configuration)
    {
        var values = new List<string>();
        values.AddRange(Split(configuration["Admin:AllowedEmails"]));
        values.AddRange(Split(configuration["Admin:Emails"]));
        values.AddRange(Split(configuration["Pismolet:AdminEmails"]));
        values.AddRange(Split(configuration["PISMOLET_ADMIN_EMAILS"]));
        values.AddRange(Split(Environment.GetEnvironmentVariable("PISMOLET_ADMIN_EMAILS")));

        foreach (var child in configuration.GetSection("Admin:AllowedEmails").GetChildren())
        {
            values.AddRange(Split(child.Value));
        }

        return values
            .Select(item => item.Trim().ToLowerInvariant())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Split(string? value) => string.IsNullOrWhiteSpace(value)
        ? Array.Empty<string>()
        : value.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

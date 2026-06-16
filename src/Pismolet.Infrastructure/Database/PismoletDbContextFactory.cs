using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pismolet.Web.Infrastructure.Database;

public sealed class PismoletDbContextFactory : IDesignTimeDbContextFactory<PismoletDbContext>
{
    public PismoletDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PISMOLET_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=pismolet;Username=pismolet;Password=pismolet";

        var options = new DbContextOptionsBuilder<PismoletDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PismoletDbContext(options);
    }
}

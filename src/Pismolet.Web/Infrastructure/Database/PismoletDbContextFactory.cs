using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pismolet.Web.Infrastructure.Database;

public sealed class PismoletDbContextFactory : IDesignTimeDbContextFactory<PismoletDbContext>
{
    public PismoletDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PismoletDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=pismolet;Username=pismolet;Password=pismolet")
            .Options;

        return new PismoletDbContext(options);
    }
}

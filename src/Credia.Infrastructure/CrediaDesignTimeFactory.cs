using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Credia.Infrastructure;

public sealed class CrediaDesignTimeFactory : IDesignTimeDbContextFactory<CrediaDbContext>
{
    public CrediaDbContext CreateDbContext(string[] args)
    {
        // This placeholder is used for scaffolding only; no connection is opened.
        var options = new DbContextOptionsBuilder<CrediaDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=CrediaDesign;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new CrediaDbContext(options);
    }
}

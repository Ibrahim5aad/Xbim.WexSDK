using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Xbim.WexServer.Persistence.EfCore;

/// <summary>
/// Design-time factory for creating XbimDbContext instances.
/// Used by EF Core tools (migrations, scaffolding) when no runtime host is available.
/// Configures SQL Server as the default provider for migrations.
/// </summary>
public class XbimDbContextFactory : IDesignTimeDbContextFactory<XbimDbContext>
{
    public XbimDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<XbimDbContext>();

        // Use SQL Server for design-time operations (migrations)
        // This ensures migrations are generated with SQL Server-compatible types
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=XbimDesignTime;Trusted_Connection=True;");

        return new XbimDbContext(optionsBuilder.Options);
    }
}

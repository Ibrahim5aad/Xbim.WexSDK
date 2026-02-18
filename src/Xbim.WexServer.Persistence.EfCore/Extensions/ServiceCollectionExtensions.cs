using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Xbim.WexServer.Persistence.EfCore.Extensions;

/// <summary>
/// Extension methods for registering persistence services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Xbim DbContext with SQLite provider for development.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string. Defaults to "Data Source=Xbim.db".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimSqlite(
        this IServiceCollection services,
        string connectionString = "Data Source=Xbim.db")
    {
        services.AddDbContext<XbimDbContext>(options =>
            options.UseSqlite(connectionString)
                   .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        return services;
    }

    /// <summary>
    /// Adds the Xbim DbContext with SQL Server provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<XbimDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }

    /// <summary>
    /// Adds the Xbim DbContext with custom options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsAction">Action to configure DbContext options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction)
    {
        services.AddDbContext<XbimDbContext>(optionsAction);

        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Octopus.Server.Persistence.EfCore.Extensions;

/// <summary>
/// Extension methods for registering persistence services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Octopus DbContext with SQLite provider for development.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string. Defaults to "Data Source=octopus.db".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusSqlite(
        this IServiceCollection services,
        string connectionString = "Data Source=octopus.db")
    {
        services.AddDbContext<OctopusDbContext>(options =>
            options.UseSqlite(connectionString));

        return services;
    }

    /// <summary>
    /// Adds the Octopus DbContext with SQL Server provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OctopusDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }

    /// <summary>
    /// Adds the Octopus DbContext with custom options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsAction">Action to configure DbContext options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction)
    {
        services.AddDbContext<OctopusDbContext>(optionsAction);

        return services;
    }
}

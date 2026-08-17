using Elsa.Persistence.Groundwork.Composition;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Providers;

/// <summary>
/// Registers one public Groundwork v2 provider connection for an Elsa host.
/// </summary>
/// <remarks>
/// These methods only compose the provider-owned connection. The provider factory owns all
/// provider-specific construction and capability decisions, including MongoDB transaction support.
/// The service provider owns and disposes the resulting connection.
/// </remarks>
public static class GroundworkProviderRegistration
{
    /// <summary>
    /// Registers a Groundwork SQLite provider connection.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="targetName">An optional physical-store target name. The default is <c>default</c>.</param>
    public static IServiceCollection AddGroundworkSqliteProvider(
        this IServiceCollection services,
        string connectionString,
        string? targetName = null) =>
        AddProvider(services, connectionString, targetName, value => new SqliteProviderFactory().Create(value));

    /// <summary>
    /// Registers a Groundwork PostgreSQL provider connection.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="targetName">An optional physical-store target name. The default is <c>default</c>.</param>
    public static IServiceCollection AddGroundworkPostgreSqlProvider(
        this IServiceCollection services,
        string connectionString,
        string? targetName = null) =>
        AddProvider(services, connectionString, targetName, value => new PostgreSqlProviderFactory().Create(value));

    /// <summary>
    /// Registers a Groundwork SQL Server provider connection.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="targetName">An optional physical-store target name. The default is <c>default</c>.</param>
    public static IServiceCollection AddGroundworkSqlServerProvider(
        this IServiceCollection services,
        string connectionString,
        string? targetName = null) =>
        AddProvider(services, connectionString, targetName, value => new SqlServerProviderFactory().Create(value));

    /// <summary>
    /// Registers a Groundwork MongoDB provider connection.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The MongoDB connection string.</param>
    /// <param name="targetName">An optional physical-store target name. The default is <c>default</c>.</param>
    /// <remarks>
    /// <see cref="MongoProviderFactory"/> reports the provider's actual transaction capability when
    /// the connection is created; this registration does not claim transaction support itself.
    /// </remarks>
    public static IServiceCollection AddGroundworkMongoDbProvider(
        this IServiceCollection services,
        string connectionString,
        string? targetName = null) =>
        AddProvider(services, connectionString, targetName, value => new MongoProviderFactory().Create(value));

    private static IServiceCollection AddProvider(
        IServiceCollection services,
        string connectionString,
        string? targetName,
        Func<string, IStorageProviderConnection> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(factory);

        return services.AddGroundworkStorageProviderConnection(
            _ => factory(connectionString),
            targetName);
    }
}

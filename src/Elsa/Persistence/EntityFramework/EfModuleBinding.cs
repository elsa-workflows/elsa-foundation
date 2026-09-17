using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// What a first-party module fixes about binding its derived context: the name it reports in errors, its migrations
/// history table and assembly, and where it connects by default. Every module applies a host's provider and connection
/// choices through here, so they accept the same provider aliases and resolve connections in the same order.
/// </summary>
public sealed record EfModuleBinding(
    string Owner,
    string HistoryTableName,
    string? MigrationsAssembly,
    string DefaultConnectionName = EfConnectionDefaults.ConnectionName,
    string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString)
{
    /// <summary>Picks what the module registers for the provider a host named.</summary>
    public T Select<T>(string provider, T sqlite, T sqlServer, T postgreSql, T mySql) =>
        EfRelationalProviderBinding.Select(provider, Owner, sqlite, sqlServer, postgreSql, mySql);

    /// <summary>Binds the named provider to the connection resolved for this module.</summary>
    public void Apply(
        DbContextOptionsBuilder builder,
        IServiceProvider services,
        string provider,
        string? connectionString,
        string? connectionName) =>
        EfRelationalProviderBinding.Use(
            builder,
            provider,
            EfConnectionDefaults.ResolveConnectionString(
                services,
                Owner,
                provider,
                connectionString,
                connectionName,
                DefaultConnectionName,
                DefaultSqliteConnectionString),
            HistoryTableName,
            MigrationsAssembly);
}

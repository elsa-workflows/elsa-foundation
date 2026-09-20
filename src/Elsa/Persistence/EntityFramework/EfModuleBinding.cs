using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>Binds the named provider to the connection, schema and payload encoding resolved for this module.</summary>
    public void Apply(
        DbContextOptionsBuilder builder,
        IServiceProvider services,
        string provider,
        string? connectionString,
        string? connectionName,
        string? schema = null)
    {
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
            MigrationsAssembly,
            EfSchema.Resolve(services, Owner, provider, schema));
        // Null means plaintext with the default threshold, and binding nothing then keeps a host that configures
        // nothing byte-identical to what it was: no extension, so no second model to cache.
        if (EfPayloadCompressionSettings.Resolve(services, Owner) is { } compression)
            builder.UseElsaPayloadCompression(compression);
    }

    /// <summary>
    /// Registers one module's provider-derived context, pooled or not.
    /// </summary>
    /// <remarks>
    /// Pooling reuses context instances across scopes, so it is safe only while a context carries nothing but its
    /// options: a context that captured a request's access context or scope would hand it to the next request.
    /// Every first-party module context is constructed from <see cref="DbContextOptions{TContext}"/> alone, which
    /// <c>ModuleContextPoolingTests</c> keeps true, so the option is offered on all of them.
    /// </remarks>
    public void AddContext<TContext>(
        IServiceCollection services,
        bool pooled,
        Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (pooled)
            services.AddDbContextPool<TContext>(configure);
        else
            services.AddDbContext<TContext>(configure);
    }
}

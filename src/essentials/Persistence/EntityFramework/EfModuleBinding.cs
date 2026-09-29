using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// What a first-party module fixes about binding its derived context: the name it reports in errors, its migrations
/// history table and assembly, and where it connects by default. Every module applies a host's provider and connection
/// choices through here, so they accept the same provider aliases and resolve connections in the same order.
/// </summary>
/// <remarks>
/// The migrations assembly is the assembly itself, never its name (spec 183, FR-021, amended 2026-09-29): EF Core would
/// resolve a name from its own load context, and where a package upgraded in place leaves the previous release loaded
/// there, the name reaches the previous release's migrations, which the new release's context does not match, so none of
/// the new release's pending migrations would be seen. <see langword="null"/> reads the context's own assembly.
/// </remarks>
public sealed record EfModuleBinding(
    string Owner,
    string HistoryTableName,
    Assembly? MigrationsAssembly,
    string DefaultConnectionName = EfConnectionDefaults.ConnectionName,
    string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString)
{
    /// <summary>
    /// Derives a module's binding from its own <see cref="EfModuleAttribute"/> declaration, so a registration
    /// states the binding once instead of constructing an equivalent of it by hand. <paramref name="contextType"/>
    /// is the module's base context — the same type its <c>[EfModule]</c> attribute names.
    /// </summary>
    public static EfModuleBinding For(Type contextType)
    {
        ArgumentNullException.ThrowIfNull(contextType);

        var descriptor = EfModuleCatalog.Discover([contextType.Assembly])
            .SingleOrDefault(candidate => candidate.ContextType == contextType);
        if (descriptor is null)
            throw new InvalidOperationException(
                $"{contextType.Assembly.GetName().Name} declares no [EfModule] whose base context is {contextType}.");

        return new EfModuleBinding(
            descriptor.Owner,
            descriptor.HistoryTableName,
            contextType.Assembly,
            descriptor.DefaultConnectionName,
            descriptor.DefaultSqliteConnectionString);
    }

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
        // Every module context holds its writes to the write version its finalization gate keeps (spec 181, FR-009).
        SchemaFinalization.EfSchemaWriteGateInterceptor.EnsureAdded(builder);
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

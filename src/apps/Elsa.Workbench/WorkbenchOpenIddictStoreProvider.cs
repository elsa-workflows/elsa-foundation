using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Persistence.EntityFramework;
using Elsa.Workbench.OpenIddict;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench;

/// <summary>
/// Elsa's selection of the engine under Workbench's OpenIddict vendor store, kept out of the vendor sources so they stay the
/// third-party store's alone (<c>OpenIddictPersistenceArchitectureTests</c>). The vendor registration binds the SQLite store, or the
/// in-memory demo one, and this selects SQL Server or PostgreSQL beside it, by the engine names every Elsa EF module takes
/// (<see cref="EfRelationalProviderBinding"/>), so the token store can sit in the database every node shares: access-token validation
/// reads the token's row, so a token issued on one node is only valid on another when both read one store.
/// </summary>
internal static class WorkbenchOpenIddictStoreProvider
{
    /// <summary>The module name its migrations history table is named for, as every Elsa EF module's is.</summary>
    internal const string Module = "OpenIddict";

    /// <summary>
    /// Registered right after <c>AddWorkbenchOpenIddictVendor</c>. The stores ask for <see cref="OpenIddictIdentityDbContext"/>, which
    /// this resolves to the selected engine's context: the SQLite store the vendor registration binds, for <c>Sqlite</c> and for the
    /// in-memory demo store, and otherwise the engine's own context, whose migrations are the engine's own.
    /// </summary>
    internal static IServiceCollection AddWorkbenchOpenIddictStoreProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkbenchOpenIddictStoreOptions>()
            .Bind(configuration.GetSection(WorkbenchOpenIddictStoreOptions.SectionPath));

        AddEngineContext<OpenIddictIdentitySqlServerDbContext>(services, "SqlServer");
        AddEngineContext<OpenIddictIdentityPostgreSqlDbContext>(services, "PostgreSql");

        services.Replace(ServiceDescriptor.Scoped<OpenIddictIdentityDbContext>(SelectContext));
        return services;
    }

    /// <summary>
    /// OpenIddict prunes with a bulk delete over a subquery with a limit, which MySQL refuses (error 1235, "doesn't yet support
    /// 'LIMIT &amp; IN/ALL/ANY/SOME subquery'") and the MySQL provider the platform supports does not rewrite. A MySQL store would
    /// grow for ever, since the prune, which a store with a row per token cannot do without, could never run: refused, not offered
    /// without it.
    /// </summary>
    private const string MySqlRefusal =
        "Workbench's OpenIddict store does not support MySQL: its provider cannot run OpenIddict's token prune, " +
        "so the store would grow without bound. Use SQLite, SqlServer or PostgreSql.";

    private static void AddEngineContext<TContext>(IServiceCollection services, string provider)
        where TContext : OpenIddictIdentityDbContext =>
        services.AddDbContext<TContext>((serviceProvider, builder) =>
        {
            var connectionString = EfConnectionDefaults.ResolveConnectionString(
                serviceProvider,
                "Workbench OpenIddict",
                provider,
                serviceProvider.GetRequiredService<IOptions<OpenIddictIdentityOptions>>().Value.ConnectionString,
                connectionName: null);
            // The tables sit in the context's own schema on the engines that have one, and the migrations carry it, so a
            // schema is not passed here: the host-wide schema setting is the Elsa modules' and does not move this store.
            EfRelationalProviderBinding.UseMigrationsFrom(
                builder,
                provider,
                connectionString,
                EfMigrationsHistory.TableName(Module),
                typeof(OpenIddictIdentityDbContext).Assembly);
        });

    private static OpenIddictIdentityDbContext SelectContext(IServiceProvider services)
    {
        if (services.GetRequiredService<IOptions<OpenIddictIdentityOptions>>().Value.IsDevelopmentOrDemo)
            return CreateVendorContext(services);

        return EfRelationalProviderBinding.Select<Func<IServiceProvider, OpenIddictIdentityDbContext>>(
            services.GetRequiredService<IOptions<WorkbenchOpenIddictStoreOptions>>().Value.Provider,
            "Workbench OpenIddict",
            CreateVendorContext,
            static provider => provider.GetRequiredService<OpenIddictIdentitySqlServerDbContext>(),
            static provider => provider.GetRequiredService<OpenIddictIdentityPostgreSqlDbContext>(),
            static _ => throw new NotSupportedException(MySqlRefusal))(services);
    }

    /// <summary>The context the vendor registration binds: SQLite, or the demo in-memory store.</summary>
    private static OpenIddictIdentityDbContext CreateVendorContext(IServiceProvider services) =>
        ActivatorUtilities.CreateInstance<OpenIddictIdentityDbContext>(services);
}

/// <summary>Which engine Workbench's OpenIddict vendor store sits on, set beside the store's connection string.</summary>
internal sealed class WorkbenchOpenIddictStoreOptions
{
    internal const string SectionPath = "CShells:Shells:default:Features:FoundationIdentityOpenIddict";

    /// <summary>
    /// The engine the store sits on, which the demo in-memory store ignores: <c>Sqlite</c> (the single-node default, in the file the
    /// store's connection string names), <c>SqlServer</c> or <c>PostgreSql</c>; <c>MySql</c> is refused. Another engine takes the
    /// store's connection string, or else <c>ConnectionStrings:Elsa</c>, the connection the Elsa modules share.
    /// </summary>
    public string Provider { get; set; } = "Sqlite";
}

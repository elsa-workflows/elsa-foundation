using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Persistence.EntityFramework;
using Elsa.Workbench.OpenIddict;
using Elsa.Workbench.OpenIddictEngines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench;

/// <summary>
/// Elsa's selection of the engine under Workbench's OpenIddict vendor store, kept out of the vendor sources so they stay the
/// third-party store's alone (<c>OpenIddictPersistenceArchitectureTests</c>). The vendor registration binds the SQLite store, or the
/// in-memory demo one, and this selects SQL Server or PostgreSQL beside it, by the engine names every Elsa EF module takes
/// (<see cref="EfRelationalProviderBinding"/>), so the token store can sit in the database every node shares: access-token validation
/// reads the token's row, so a token issued on one node is only valid on another when both read one store.
/// </summary>
public static class WorkbenchOpenIddictStoreProvider
{
    /// <summary>The module name its migrations history table is named for, as every Elsa EF module's is.</summary>
    public const string Module = "OpenIddict";

    private const string Owner = "Workbench OpenIddict";

    /// <summary>
    /// OpenIddict prunes with a bulk delete over a subquery with a limit, which MySQL refuses (error 1235, "doesn't yet support
    /// 'LIMIT &amp; IN/ALL/ANY/SOME subquery'") and the MySQL provider the platform supports does not rewrite. A MySQL store would
    /// grow for ever, since the prune, which a store with a row per token cannot do without, could never run: refused, not offered
    /// without it.
    /// </summary>
    private const string MySqlRefusal =
        "Workbench's OpenIddict store does not support MySQL: its provider cannot run OpenIddict's token prune, " +
        "so the store would grow without bound. Use SQLite, SqlServer or PostgreSql.";

    /// <summary>
    /// Registered right after <c>AddWorkbenchOpenIddictVendor</c>. The stores ask for <see cref="OpenIddictIdentityDbContext"/>, which
    /// this resolves to the selected engine's context: the SQLite store the vendor registration binds, for <c>Sqlite</c> and for the
    /// in-memory demo store, and otherwise the engine's own context, whose migrations are the engine's own. The host's start refuses
    /// a <c>Provider</c> that is not supported, and warns of the settings that leave the store per node.
    /// </summary>
    public static IServiceCollection AddWorkbenchOpenIddictStoreProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkbenchOpenIddictStoreOptions>()
            .Bind(configuration.GetSection(WorkbenchOpenIddictStoreOptions.SectionPath))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkbenchOpenIddictStoreOptions>, WorkbenchOpenIddictStoreOptionsValidator>();
        services.AddHostedService(provider => new WorkbenchOpenIddictStoreNotices(
            provider.GetRequiredService<IOptions<OpenIddictIdentityOptions>>(),
            provider.GetRequiredService<IOptions<WorkbenchOpenIddictStoreOptions>>(),
            configuration,
            provider.GetRequiredService<ILogger<WorkbenchOpenIddictStoreNotices>>()));

        AddEngineContext<OpenIddictIdentitySqlServerDbContext>(services, "SqlServer");
        AddEngineContext<OpenIddictIdentityPostgreSqlDbContext>(services, "PostgreSql");

        services.Replace(ServiceDescriptor.Scoped<OpenIddictIdentityDbContext>(SelectContext));
        return services;
    }

    /// <summary>Why <paramref name="provider"/> cannot be the store's engine, or <see langword="null"/> when it can; unset is SQLite.</summary>
    internal static string? Refusal(string? provider)
    {
        try
        {
            return EfRelationalProviderBinding.Select<string?>(
                string.IsNullOrWhiteSpace(provider) ? "Sqlite" : provider, Owner, null, null, null, MySqlRefusal);
        }
        catch (ArgumentException)
        {
            return $"Workbench's OpenIddict store does not know the provider '{provider}'. " +
                   $"Set {WorkbenchOpenIddictStoreOptions.SectionPath}:Provider to Sqlite, SqlServer or PostgreSql.";
        }
    }

    private static void AddEngineContext<TContext>(IServiceCollection services, string provider)
        where TContext : OpenIddictIdentityDbContext =>
        services.AddDbContext<TContext>((serviceProvider, builder) =>
        {
            var connectionString = EfConnectionDefaults.ResolveConnectionString(
                serviceProvider,
                Owner,
                provider,
                serviceProvider.GetRequiredService<IOptions<OpenIddictIdentityOptions>>().Value.ConnectionString,
                connectionName: null);
            // The history table sits in the schema of the tables it records, as the vendor registration puts the SQLite one and
            // as every Elsa EF module's does (EfRelationalProviderBinding): the migrations are scaffolded with the context's own
            // schema, so the history a migrate reads is the history of the tables beside it. That is the context's schema, not
            // the host-wide Elsa schema setting, which does not move this store.
            EfRelationalProviderBinding.UseMigrationsFrom(
                builder,
                provider,
                connectionString,
                EfMigrationsHistory.TableName(Module),
                typeof(OpenIddictIdentityDbContext).Assembly,
                OpenIddictIdentityDbContext.Schema);
        });

    private static OpenIddictIdentityDbContext SelectContext(IServiceProvider services)
    {
        if (services.GetRequiredService<IOptions<OpenIddictIdentityOptions>>().Value.IsDevelopmentOrDemo)
            return CreateVendorContext(services);

        var provider = services.GetRequiredService<IOptions<WorkbenchOpenIddictStoreOptions>>().Value.Provider;
        return EfRelationalProviderBinding.Select<Func<IServiceProvider, OpenIddictIdentityDbContext>>(
            string.IsNullOrWhiteSpace(provider) ? "Sqlite" : provider,
            Owner,
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
public sealed class WorkbenchOpenIddictStoreOptions
{
    /// <summary>The default shell's OpenIddict settings, where the store's other settings (<c>ConnectionString</c>, <c>AutoMigrate</c>) are.</summary>
    public const string SectionPath = "CShells:Shells:default:Features:FoundationIdentityOpenIddict";

    /// <summary>
    /// The engine the store sits on, which the demo in-memory store ignores: <c>Sqlite</c> (the single-node default, in the file the
    /// store's connection string names), <c>SqlServer</c> or <c>PostgreSql</c>; <c>MySql</c> is refused. Unset is SQLite, and stays
    /// SQLite whatever the platform's persistence provider is: the store moves only when this is set. Another engine takes the
    /// store's connection string, or else <c>ConnectionStrings:Elsa</c>, the connection the Elsa modules share.
    /// </summary>
    public string? Provider { get; set; }
}

/// <summary>Fails the host's start for a <see cref="WorkbenchOpenIddictStoreOptions.Provider"/> the store cannot use, MySQL included, instead of its first use.</summary>
internal sealed class WorkbenchOpenIddictStoreOptionsValidator : IValidateOptions<WorkbenchOpenIddictStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkbenchOpenIddictStoreOptions options) =>
        WorkbenchOpenIddictStoreProvider.Refusal(options.Provider) is { } refusal ? ValidateOptionsResult.Fail(refusal) : ValidateOptionsResult.Success;
}

/// <summary>
/// Warns at the host's start of the two settings that leave the token store somewhere its operator may not expect, neither of which
/// is an error: the store is only moved off its per-node SQLite by an explicit <c>Provider</c>, so an existing store never moves
/// silently, and the demo store ignores the engine.
/// </summary>
internal sealed class WorkbenchOpenIddictStoreNotices(
    IOptions<OpenIddictIdentityOptions> identity,
    IOptions<WorkbenchOpenIddictStoreOptions> store,
    IConfiguration configuration,
    ILogger<WorkbenchOpenIddictStoreNotices> logger) : IHostedService
{
    private const string Setting = $"{WorkbenchOpenIddictStoreOptions.SectionPath}:Provider";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var provider = store.Value.Provider;
        var set = !string.IsNullOrWhiteSpace(provider);
        if (identity.Value.IsDevelopmentOrDemo)
        {
            if (set)
                logger.LogWarning(
                    "{Setting} is '{Provider}', but IsDevelopmentOrDemo is on, so the OpenIddict token store is the in-memory demo store: per node, and lost when the host stops. The provider is ignored.",
                    Setting, provider);
        }
        else if (!set && PlatformProvider() is { } platform)
        {
            logger.LogWarning(
                "The platform's persistence provider is {Platform}, but {Setting} is not set, so the OpenIddict token store is still a per-node SQLite file: a token issued on one node is not valid on another. Set {Setting} to share the store.",
                platform, Setting, Setting);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The provider of the platform's default persistence resource (<c>Elsa:Persistence:DefaultResource</c>) when it is not SQLite.</summary>
    private string? PlatformProvider()
    {
        var persistence = configuration.GetSection("Elsa:Persistence");
        if (persistence["DefaultResource"] is not { Length: > 0 } resource ||
            persistence.GetSection("Resources").GetSection(resource)["Provider"] is not { Length: > 0 } provider)
            return null;

        return EfRelationalProviderBinding.Normalize(provider) == "sqlite" ? null : provider;
    }
}

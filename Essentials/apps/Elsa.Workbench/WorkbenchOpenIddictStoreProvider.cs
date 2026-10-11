using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
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
            provider.GetService<IEfToolingShellDefaults>(),
            provider.GetService<IHostApplicationLifetime>(),
            provider.GetRequiredService<ILogger<WorkbenchOpenIddictStoreNotices>>()));

        AddEngineContext<OpenIddictIdentitySqlServerDbContext>(services, "SqlServer");
        AddEngineContext<OpenIddictIdentityPostgreSqlDbContext>(services, "PostgreSql");

        services.Replace(ServiceDescriptor.Scoped<OpenIddictIdentityDbContext>(SelectContext));
        return services;
    }

    /// <summary>Why <paramref name="provider"/> cannot be the store's engine, or <see langword="null"/> when it can; unset is SQLite.</summary>
    public static string? Refusal(string? provider)
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
public sealed class WorkbenchOpenIddictStoreOptionsValidator : IValidateOptions<WorkbenchOpenIddictStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkbenchOpenIddictStoreOptions options) =>
        WorkbenchOpenIddictStoreProvider.Refusal(options.Provider) is { } refusal ? ValidateOptionsResult.Fail(refusal) : ValidateOptionsResult.Success;
}

/// <summary>
/// Warns at the host's start of the two settings that leave the token store somewhere its operator may not expect, neither of which
/// is an error: the store is only moved off its per-node SQLite by an explicit <c>Provider</c>, so an existing store never moves
/// silently, and the demo store ignores the engine.
/// </summary>
/// <remarks>
/// The platform's providers are those of the default shell's EF consumers as the platform resolves them
/// (<see cref="WorkbenchOpenIddictPlatformProviders"/>): the root and shell default resources, per-feature bindings and the legacy
/// per-feature <c>Provider</c> settings. That needs the host's loaded features, so it is read once the host has started, not while it
/// is starting; a consumer a shell enables later, or a setting changed after the start, is not seen.
/// </remarks>
public sealed class WorkbenchOpenIddictStoreNotices(
    IOptions<OpenIddictIdentityOptions> identity,
    IOptions<WorkbenchOpenIddictStoreOptions> store,
    IConfiguration configuration,
    IEfToolingShellDefaults? hostDefaults,
    IHostApplicationLifetime? lifetime,
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
        else if (!set)
        {
            if (lifetime is null)
                NoticePlatformProvider();
            else
                lifetime.ApplicationStarted.Register(NoticePlatformProvider);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Warns when the platform's EF consumers are on an engine other than SQLite and the store, whose own provider is unset, is not.</summary>
    public void NoticePlatformProvider()
    {
        IReadOnlyCollection<string> platform;
        try
        {
            if (hostDefaults is null)
                return;
            platform = WorkbenchOpenIddictPlatformProviders.Resolve(configuration, hostDefaults, AppDomain.CurrentDomain.GetAssemblies());
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // The notice is advice: a platform provider that cannot be read is not a reason to say anything, or to fail.
            logger.LogInformation(failure, "The platform's persistence providers could not be read, so the OpenIddict token store is not compared with them.");
            return;
        }

        var others = platform.Where(provider => EfRelationalProviderBinding.Normalize(provider) != "sqlite").ToArray();
        if (others.Length == 0)
            return;

        var shareable = others.FirstOrDefault(provider => WorkbenchOpenIddictStoreProvider.Refusal(provider) is null);
        if (shareable is null)
            logger.LogWarning(
                "The platform's persistence provider is {Platform}, which the OpenIddict token store does not support, so the token store is still a per-node SQLite file: a token issued on one node is not valid on another. Sharing it needs a SQL Server or PostgreSQL database, selected with {Setting}.",
                string.Join(", ", others), Setting);
        else
            logger.LogWarning(
                "The platform's persistence provider is {Platform}, but {Setting} is not set, so the OpenIddict token store is still a per-node SQLite file: a token issued on one node is not valid on another. Set {Setting} to {Provider} to share the store.",
                string.Join(", ", others), Setting, Setting, shareable);
    }
}

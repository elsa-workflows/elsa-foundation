using CShells.Features;
using Elsa.Events.Core.Contracts;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Handlers;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Services;
using Elsa.Persistence.EFCore.Tasks;
using Elsa.Primitives.Contracts;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Elsa.Persistence.EFCore;

public abstract class EFCorePersistenceShellFeatureBase<TDbContext> : IShellFeature
    where TDbContext : DbContext
{
    /// <summary>
    /// Gets or sets a value indicating whether to use context pooling.
    /// When not explicitly set, falls back to shared settings if available.
    /// </summary>
    [ManifestSetting(DisplayName = "Use context pooling", Description = "Controls whether EF Core DbContext pooling is enabled for this persistence feature.", Category = "Persistence", Advanced = true)]
    public bool? UseContextPooling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to run migrations.
    /// When not explicitly set, falls back to shared settings if available, defaulting to true.
    /// </summary>
    [ManifestSetting(DisplayName = "Run migrations", Description = "Controls whether database migrations run during startup.", Category = "Persistence", DefaultValue = "true")]
    public bool? RunMigrations { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of the <see cref="IDbContextFactory{TContext}"/>.
    /// When not explicitly set, falls back to shared settings if available, defaulting to <see cref="ServiceLifetime.Scoped"/>.
    /// </summary>
    [ManifestSetting(DisplayName = "DbContext factory lifetime", Description = "Service lifetime used for the EF Core DbContext factory.", Category = "Persistence", Advanced = true)]
    public ServiceLifetime? DbContextFactoryLifetime { get; set; }

    /// <summary>
    /// Gets or sets the connection string to use for the database.
    /// When not explicitly set, falls back to shared settings if available.
    /// </summary>
    [ManifestSetting(DisplayName = "Connection string", Description = "Database connection string used by this persistence feature.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Controls whether command-side persistence services are registered.
    /// </summary>
    [ManifestSetting(DisplayName = "Use commands", Description = "Registers command-side EF Core persistence services and entity saving handlers.", Category = "Persistence", DefaultValue = "true", Advanced = true)]
    public bool UseCommands { get; set; } = true;

    /// <summary>
    /// Controls whether query-side persistence services are registered.
    /// </summary>
    [ManifestSetting(DisplayName = "Use queries", Description = "Registers query-side EF Core persistence services and entity loading handlers.", Category = "Persistence", DefaultValue = "true", Advanced = true)]
    public bool UseQueries { get; set; } = true;

    /// <summary>
    /// Assemblies scanned for the typed <see cref="Contracts.IEntitySavingHandler{TDbContext,TEntity}"/>
    /// / <see cref="Contracts.IEntityLoadingHandler{TDbContext,TEntity}"/> contributors. Both handler
    /// kinds are registered from the SAME list (saving under <see cref="UseCommands"/>, loading under
    /// <see cref="UseQueries"/>), so a feature can never register one direction without the other.
    /// Defaults to the concrete feature's own assembly; a domain base overrides this to add the
    /// assembly that actually holds its handlers (typically the intermediate <c>.EFCore</c> assembly).
    /// </summary>
    protected virtual IEnumerable<Assembly> EntityHandlerAssemblies => [GetType().Assembly];

    /// <summary>
    /// Gets or sets additional options to configure the database context.
    /// When not explicitly set, falls back to shared settings if available.
    /// </summary>
    [ManifestIgnore]
    public ElsaDbContextOptions? DbContextOptions { get; set; }

    /// <summary>
    /// Gets or sets the callback used to configure the <see cref="DbContextOptionsBuilder"/>.
    /// </summary>
    protected virtual Action<IServiceProvider, DbContextOptionsBuilder> DbContextOptionsBuilder { get; set; } = (_, _) => { };

    public virtual void ConfigureServices(IServiceCollection services)
    {
        OnBeforeConfiguring(services);

        // General services
        services.TryAddScoped<IIdentityGenerator, EFCoreIdentityGenerator>();

        // The two single aggregating event handlers that dispatch the typed
        // IEntitySavingHandler<,> / IEntityLoadingHandler<,> contributors (the draft-validator
        // shape). This base runs once per concrete persistence feature; TryAddEnumerable dedupes
        // by implementation type so each aggregator lands exactly once even with multiple
        // EF Core persistence features enabled (AddEventHandler is additive and would double-dispatch).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler, ApplyEntitySavingHandlers>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler, ApplyEntityLoadingHandlers>());

        // Resolve pooling and lifetime settings with fallback
        // Note: These are resolved at configuration time, not runtime, but they'll use defaults if not set
        var useContextPooling = UseContextPooling ?? false;

        if (useContextPooling)
            services.AddPooledDbContextFactory<TDbContext>(SetupDbContextOptionsBuilder);
        else
            services.AddDbContextFactory<TDbContext>(SetupDbContextOptionsBuilder, DbContextFactoryLifetime ?? ServiceLifetime.Scoped);

        // MIGRATION
        var runMigrations = RunMigrations ?? true;
        services.Configure<MigrationOptions>(options =>
            {
                options.RunMigrations[$"{typeof(TDbContext).FullName}"] = runMigrations;
            })
            .AddScoped<IStartupTask, RunMigrationsStartupTask<TDbContext>>();

        // COMMANDS & QUERIES. The typed entity handlers are registered here from the SAME
        // assembly list for both directions — saving with the command side, loading with the
        // query side — so a feature can never wire one direction without the other (the
        // ApplyEntity{Saving,Loading}Handlers aggregators above dispatch whatever is registered).
        var handlerAssemblies = EntityHandlerAssemblies.Distinct().ToArray();

        if (UseCommands)
        {
            services.ConfigureCommands<TDbContext>();
            foreach (var assembly in handlerAssemblies)
                services.AddEntitySavingHandlersFrom(assembly);
        }
        if (UseQueries)
        {
            foreach (var assembly in handlerAssemblies)
                services.AddEntityLoadingHandlersFrom(assembly);
        }

        OnAfterConfigured(services);
    }

    // Resolve effective settings at runtime, falling back to shared settings
    private void SetupDbContextOptionsBuilder(IServiceProvider serviceProvider, DbContextOptionsBuilder dbContextOptionsBuilder)
    {
        var sharedSettings = serviceProvider.GetService<IOptions<SharedPersistenceSettings>>()?.Value;

        var connectionString = ConnectionString
                               ?? sharedSettings?.ConnectionString
                               ?? throw new InvalidOperationException(
                                   $"Connection string not configured for {GetType().Name}. " +
                                   $"Either configure the feature directly or provide shared settings via the combined persistence feature.");

        var dbContextOptions = DbContextOptions ?? sharedSettings?.DbContextOptions;

        dbContextOptionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        // Configure the database provider
        var migrationsAssembly = GetMigrationsAssembly();
        ConfigureProvider(dbContextOptionsBuilder, migrationsAssembly, connectionString, dbContextOptions);

        // Allow derived classes to further configure
        DbContextOptionsBuilder(serviceProvider, dbContextOptionsBuilder);
    }

    /// <summary>
    /// Gets the assembly containing migrations for this provider.
    /// By default, returns the assembly of the concrete feature type.
    /// </summary>
    protected virtual Assembly GetMigrationsAssembly() => GetType().Assembly;

    /// <summary>
    /// Configures the database provider for the specified <see cref="DbContextOptionsBuilder"/>.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="migrationsAssembly">The assembly containing migrations.</param>
    /// <param name="connectionString">The connection string to use.</param>
    /// <param name="options">Additional options to configure the database context.</param>
    protected abstract void ConfigureProvider(
        DbContextOptionsBuilder builder,
        Assembly migrationsAssembly,
        string connectionString,
        ElsaDbContextOptions? options
    );

    protected virtual void OnAfterConfigured(IServiceCollection services)
    {
    }

    protected virtual void OnBeforeConfiguring(IServiceCollection services)
    {
    }
}

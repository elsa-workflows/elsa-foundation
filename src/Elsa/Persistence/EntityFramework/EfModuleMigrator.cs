using CShells.Lifecycle;
using CShells.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Applies or validates one module context's migrations on both lifecycle hooks: CShells activates and
/// reloads shells through <see cref="IShellInitializer"/>, while plain hosts start <see cref="IHostedService"/>s.
/// The host-wide <see cref="EfMigrateOptions"/> choose between auto-migrate and fail-closed validate, bound from
/// <see cref="EfMigrateOptions.SectionName"/>, so an operator can apply migrations out of process and start hosts
/// in validate mode without a code change.
/// </summary>
public sealed class EfModuleMigrator<TContext>(
    IServiceScopeFactory scopes,
    EfModuleMigration<TContext> migration,
    IOptions<EfMigrateOptions> options) : IHostedService, IShellInitializer
    where TContext : DbContext
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => ApplyAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        // A later backend switch can replace this module's EF registration; nothing is left to migrate then.
        var context = scope.ServiceProvider.GetService<TContext>();
        if (context is null)
            return;
        // MigrateAsync takes EF's migration lock, and applying is idempotent, so running on both hooks is safe.
        await EfDatabaseMigrator.ApplyAsync(context, migration.ExpectedProviderName, options.Value.Policy, cancellationToken);
        // Under both policies (FR-056), and only ever after the schema is known to be current: an audit that
        // read a pre-migration schema would answer a question about a database that no longer exists. Under
        // Validate the line above has already thrown for a pending migration, so reaching here means current.
        await EfPostMigrationActions.EnsureNotRequiredAsync(
            context,
            migration.Module,
            migration.Provider,
            migration.PostMigration,
            cancellationToken);
    }
}

/// <summary>
/// The provider a module context must be bound to before its migrations run, and what its
/// <c>[EfModule]</c> declares for after they have (ADR 0076 D8). Resolved once, at registration, so a
/// declaration this build cannot honour is refused while a host is still wiring itself up rather than
/// mid-migrate.
/// </summary>
public sealed record EfModuleMigration<TContext>(
    string ExpectedProviderName,
    string Module,
    string Provider,
    IReadOnlyList<IEfPostMigrationAction> PostMigration) where TContext : DbContext;

public static class EfModuleMigrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the migrator for <typeparamref name="TContext"/>, the base context a module resolves, whose
    /// provider-derived registration must use <paramref name="provider"/>. Repeat calls keep one migrator.
    /// </summary>
    public static IServiceCollection AddEfModuleMigrations<TContext>(this IServiceCollection services, string provider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        // The module's own [EfModule], when it has one: a test context or a context registered outside the
        // descriptor declares no post-migration action, so its absence is "nothing to audit", not a fault.
        var descriptor = EfModuleCatalog.Discover([typeof(TContext).Assembly])
            .SingleOrDefault(candidate => candidate.ContextType == typeof(TContext));
        var module = descriptor?.Name ?? typeof(TContext).Name;
        var migration = new EfModuleMigration<TContext>(
            EfRelationalProviderBinding.ExpectedProviderName(provider),
            module,
            EfRelationalProviderBinding.Select(provider, module, "Sqlite", "SqlServer", "PostgreSql", "MySql"),
            EfPostMigrationActions.Create(module, descriptor?.PostMigration ?? []));
        // Registered first so the validator that reports a missing engine starts ahead of every migrator.
        services.AddEfProviderBindingValidation<TContext>(provider);
        foreach (var existing in services.Where(descriptor => descriptor.ServiceType == typeof(EfModuleMigration<TContext>)).ToArray())
            services.Remove(existing);
        services.AddSingleton(migration);
        services.AddOptions<EfMigrateOptions>();
        // One binding per container however many modules register a migrator; a host that configures the policy in
        // code after this call still wins, because IConfigureOptions run in registration order.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<EfMigrateOptions>, EfMigrateOptionsConfigurator>());
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EfModuleMigrator<TContext>)))
            return services;
        // CShells runs initializers by lifecycle phase, not registration order, and shell tasks and seeders
        // run at Start. Schema has to exist before any of them touches a store, so migrations run at Prepare.
        services.AddShellInitializer<EfModuleMigrator<TContext>>(LifecyclePhase.Prepare, 0);
        // AddShellInitializer registers the initializer transiently; this last-wins registration makes the
        // shell and the hosted-service paths resolve one instance, exactly as AddEfProviderBindingValidation
        // does for the validator above. Ordering matters: a singleton added *before* that call is shadowed by
        // the transient descriptor it appends, which would hand each hook a migrator of its own.
        services.AddSingleton<EfModuleMigrator<TContext>>();
        // Plain hosts have no shell lifecycle; there the hosted service is what applies the migrations.
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<EfModuleMigrator<TContext>>());
        return services;
    }

}

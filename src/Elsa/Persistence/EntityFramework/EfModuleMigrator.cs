using CShells.Lifecycle;
using CShells.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Applies or validates one module context's migrations on both lifecycle hooks: CShells activates and
/// reloads shells through <see cref="IShellInitializer"/>, while plain hosts start <see cref="IHostedService"/>s.
/// The host-wide <see cref="EfMigrateOptions"/> choose between auto-migrate and fail-closed validate, so an
/// operator can apply migrations out of process and start hosts in validate mode.
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
    }
}

/// <summary>The provider a module context must be bound to before its migrations run.</summary>
public sealed record EfModuleMigration<TContext>(string ExpectedProviderName) where TContext : DbContext;

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
        var migration = new EfModuleMigration<TContext>(EfRelationalProviderBinding.ExpectedProviderName(provider));
        foreach (var existing in services.Where(descriptor => descriptor.ServiceType == typeof(EfModuleMigration<TContext>)).ToArray())
            services.Remove(existing);
        services.AddSingleton(migration);
        services.AddOptions<EfMigrateOptions>();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EfModuleMigrator<TContext>)))
            return services;
        services.AddSingleton<EfModuleMigrator<TContext>>();
        // CShells runs initializers by lifecycle phase, not registration order, and shell tasks and seeders
        // run at Start. Schema has to exist before any of them touches a store, so migrations run at Prepare.
        services.AddShellInitializer<EfModuleMigrator<TContext>>(LifecyclePhase.Prepare, 0);
        // Plain hosts have no shell lifecycle; there the hosted service is what applies the migrations.
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<EfModuleMigrator<TContext>>());
        return services;
    }

}

using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

/// <summary>
/// Applies or validates Secrets EF migrations on both lifecycle hooks CShells and plain hosts use.
/// Shell-scoped <see cref="IHostedService"/>s do not run; CShells activates and reloads through
/// <see cref="IShellInitializer"/>. The same instance is registered under both interfaces so a
/// feature enable or reload applies <see cref="EfMigratePolicy"/> without booting a second stack.
/// </summary>
public sealed class SecretsEfMigrationHostedService(
    IServiceScopeFactory scopes,
    SecretsEntityFrameworkCoreOptions options) : IHostedService, IShellInitializer
{
    private int applied;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        // Same instance can be resolved as both IHostedService and IShellInitializer. MigrateAsync
        // is idempotent; skip the second call on this instance so a copied root descriptor cannot
        // start two concurrent applies. A CShells reload builds a new provider and a new instance.
        if (Interlocked.Exchange(ref applied, 1) == 1)
            return;

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SecretsDbContext>();
        var expected = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
        await EfDatabaseMigrator.ApplyAsync(context, expected, options.MigratePolicy, cancellationToken);
    }
}

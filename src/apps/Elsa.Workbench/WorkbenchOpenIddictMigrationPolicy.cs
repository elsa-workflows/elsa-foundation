using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Persistence.EntityFramework;
using Elsa.Workbench.OpenIddict;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench;

/// <summary>
/// Elsa's migration policy for Workbench's OpenIddict vendor store, kept out of the vendor sources so they stay the third-party
/// store's alone (<c>OpenIddictPersistenceArchitectureTests</c>): the vendor initializer migrates only when the store's
/// <c>AutoMigrate</c> is on, so this takes that over by turning it off for the vendor and migrating itself, through
/// <see cref="EfSqliteMigrationLock"/>. A SQLite store whose EF migration lock a killed process left behind then fails the host's
/// start with the way to clear it, or, with nothing pending, goes on past it, instead of hanging it (#2196). The vendor
/// initializer is left to create the demo in-memory store and nothing else.
/// </summary>
internal static class WorkbenchOpenIddictMigrationPolicy
{
    /// <summary>
    /// Registered right after <c>AddWorkbenchOpenIddictVendor</c>, so the store is migrated at the same point in the host's start
    /// as the vendor initializer used to migrate it (a test over the real host holds the order). The root hosted-service shape is
    /// the vendor initializer's: CShells copies root descriptors into shell providers, so a shell initializer would migrate again
    /// on activation.
    /// </summary>
    internal static IServiceCollection AddWorkbenchOpenIddictMigrationPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<WorkbenchOpenIddictMigrationSwitch>();
        services.AddSingleton<IPostConfigureOptions<OpenIddictIdentityOptions>, TakeOverAutoMigrate>();
        services.AddSingleton(provider => new WorkbenchOpenIddictMigrator(
            provider.GetRequiredService<IOptions<OpenIddictIdentityOptions>>(),
            provider.GetRequiredService<WorkbenchOpenIddictMigrationSwitch>(),
            provider,
            EfMigrateOptions.FromConfiguration(configuration)));
        services.AddHostedService(provider => provider.GetRequiredService<WorkbenchOpenIddictMigrator>());
        return services;
    }
}

/// <summary>Whether the store's <c>AutoMigrate</c> asked for migration, as the options pipeline resolved it before the vendor was told otherwise.</summary>
internal sealed class WorkbenchOpenIddictMigrationSwitch
{
    public bool AutoMigrate { get; set; } = true;
}

/// <summary>
/// Takes the store's <c>AutoMigrate</c> over from the vendor initializer: it records what every configuration of the options
/// resolved, in the one place that overrides it, and turns it off for the vendor. A post-configuration sees the value after all of
/// them, so a setting made in code is honoured as one made in configuration is.
/// </summary>
internal sealed class TakeOverAutoMigrate(WorkbenchOpenIddictMigrationSwitch migration) : IPostConfigureOptions<OpenIddictIdentityOptions>
{
    public void PostConfigure(string? name, OpenIddictIdentityOptions options)
    {
        migration.AutoMigrate = options.AutoMigrate;
        options.AutoMigrate = false;
    }
}

/// <summary>Migrates the durable OpenIddict store at the host's start, as the store's own <c>AutoMigrate</c> asked.</summary>
internal sealed class WorkbenchOpenIddictMigrator : IHostedService
{
    private readonly WorkbenchOpenIddictMigrationSwitch _migration;
    private readonly IServiceProvider _services;
    private readonly EfMigrateOptions _options;

    /// <param name="identity">
    /// The options the switch is recorded from: resolved here, so that its post-configuration has run, and the switch holds what the
    /// options said, by the time <see cref="StartAsync"/> reads it.
    /// </param>
    public WorkbenchOpenIddictMigrator(
        IOptions<OpenIddictIdentityOptions> identity,
        WorkbenchOpenIddictMigrationSwitch migration,
        IServiceProvider services,
        EfMigrateOptions options)
    {
        _ = identity.Value;
        _migration = migration;
        _services = services;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_migration.AutoMigrate)
            return;

        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();
        // Every provider that has migrations goes through the lock policy, which hands all but SQLite straight to MigrateAsync. The
        // in-memory demo store has none, and the vendor initializer creates it.
        if (EfDatabaseMigrator.UsesMigrations(store))
            await EfSqliteMigrationLock.MigrateAsync(store, _options, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

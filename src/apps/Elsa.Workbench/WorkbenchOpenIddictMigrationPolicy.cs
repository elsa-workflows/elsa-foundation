using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Persistence.EntityFramework;
using Elsa.Workbench.OpenIddict;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    private const string FeatureSection = "CShells:Shells:default:Features:FoundationIdentityOpenIddict";

    /// <summary>
    /// Registered right after <c>AddWorkbenchOpenIddictVendor</c>, so the store is migrated at the same point in the host's start
    /// as the vendor initializer used to migrate it. The root hosted-service shape is the vendor initializer's: CShells copies root
    /// descriptors into shell providers, so a shell initializer would migrate again on activation.
    /// </summary>
    internal static IServiceCollection AddWorkbenchOpenIddictMigrationPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        services.PostConfigure<OpenIddictIdentityOptions>(options => options.AutoMigrate = false);
        services.AddSingleton(provider => new WorkbenchOpenIddictMigrator(
            provider,
            (configuration.GetSection(FeatureSection).Get<OpenIddictIdentityOptions>() ?? new()).AutoMigrate,
            EfMigrateOptions.FromConfiguration(configuration)));
        services.AddHostedService(provider => provider.GetRequiredService<WorkbenchOpenIddictMigrator>());
        return services;
    }
}

/// <summary>Migrates the durable SQLite OpenIddict store at the host's start, as the store's own <c>AutoMigrate</c> asks.</summary>
internal sealed class WorkbenchOpenIddictMigrator(IServiceProvider services, bool autoMigrate, EfMigrateOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!autoMigrate)
            return;

        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();
        // The in-memory demo store has no migrations, and the vendor initializer creates it; this host registers no other provider.
        if (store.Database.ProviderName == EfProviderNames.Sqlite)
            await EfSqliteMigrationLock.MigrateAsync(store, options, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

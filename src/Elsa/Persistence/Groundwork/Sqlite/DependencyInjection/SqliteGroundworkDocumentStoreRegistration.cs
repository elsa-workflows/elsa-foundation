using CShells.Lifecycle;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;

/// <summary>
/// Shared wiring that registers the one SQLite-backed Groundwork <see cref="IDocumentStore"/> for both the
/// runtime-only and unified provider registrations. The store is materialized once at host startup by a
/// <see cref="SqliteGroundworkDocumentStoreInitializer"/> (run as both a hosted service and a CShells shell
/// initializer, in the <see cref="LifecyclePhase.Prepare"/> phase) which populates a shared
/// <see cref="GroundworkDocumentStoreHolder"/>; <see cref="IDocumentStore"/> resolves from that holder, so
/// consumers get a fully-initialized singleton with no synchronous block on the resolving thread.
/// </summary>
public static class SqliteGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddSqliteGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString,
        StorageManifest manifest,
        ProviderIdentity provider)
    {
        services.RemoveAll<IDocumentStore>();
        services.AddSingleton<GroundworkDocumentStoreHolder>();
        services.AddSingleton(sp => new SqliteGroundworkDocumentStoreInitializer(
            connectionString,
            manifest,
            provider,
            sp.GetRequiredService<GroundworkDocumentStoreHolder>()));
        services.AddHostedService(sp => sp.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(SqliteGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: 0,
            IsExplicit: true,
            Source: $"{nameof(SqliteGroundworkDocumentStoreRegistration)}.{nameof(AddSqliteGroundworkDocumentStore)}"));
        services.AddSingleton<IDocumentStore>(sp => sp.GetRequiredService<GroundworkDocumentStoreHolder>().Store);

        return services;
    }
}

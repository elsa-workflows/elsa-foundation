using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.Documents;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Opens provider stores on Groundwork's physical surface and pairs each with the route-bound bounded-query
/// runtime a production host obtains from its provider initializer. Every test driver and fixture that needs a
/// real store shares this recipe, so "admit the target, then compile one query runtime per storage route" is
/// expressed once rather than re-derived per suite.
/// </summary>
public static class GroundworkPhysicalTestStores
{
    /// <summary>
    /// Test hosts own their database outright, so pending safe schema work is applied when the store opens
    /// rather than requiring a separate application step.
    /// </summary>
    public static GroundworkRuntimeSchemaAdmissionOptions AutoApply => new() { AutoApplyOnStartup = true };

    public static async Task<GroundworkPhysicalTestStore<SqlitePhysicalDocumentStore>> OpenSqliteAsync(
        string connectionString,
        StorageManifest manifest,
        ProviderIdentity provider,
        DocumentStoreAccess access,
        CancellationToken cancellationToken = default)
    {
        var store = await SqliteDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            access,
            options: AutoApply,
            cancellationToken: cancellationToken);
        return new GroundworkPhysicalTestStore<SqlitePhysicalDocumentStore>(
            store,
            SqliteQueries(store, manifest, provider));
    }

    public static GroundworkBoundedDocumentStoreRouter SqliteQueries(
        SqlitePhysicalDocumentStore store,
        StorageManifest manifest,
        ProviderIdentity provider)
    {
        var target = PhysicalSchemaTargetCompiler.Compile(
            manifest,
            provider,
            SqliteGroundworkCapabilities.PhysicalNames);
        return Route(
            target.Routes,
            route => SqlitePhysicalQueryRuntime.Create(store, manifest, route, target.Provider));
    }

    public static GroundworkBoundedDocumentStoreRouter SqliteQueries(
        SqlitePhysicalDocumentStore store,
        StorageManifest manifest,
        PhysicalSchemaTarget target) =>
        Route(
            target.Routes,
            route => SqlitePhysicalQueryRuntime.Create(store, manifest, route, target.Provider));

    public static async Task<GroundworkPhysicalTestStore<PostgreSqlPhysicalDocumentStore>> OpenPostgreSqlAsync(
        string connectionString,
        StorageManifest manifest,
        ProviderIdentity provider,
        DocumentStoreAccess access,
        CancellationToken cancellationToken = default)
    {
        var store = await PostgreSqlDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            access,
            options: AutoApply,
            cancellationToken: cancellationToken);
        var target = PhysicalSchemaTargetCompiler.Compile(
            manifest,
            provider,
            PostgreSqlGroundworkCapabilities.PhysicalNames);
        return new GroundworkPhysicalTestStore<PostgreSqlPhysicalDocumentStore>(
            store,
            PostgreSqlQueries(store, manifest, target));
    }

    public static GroundworkBoundedDocumentStoreRouter PostgreSqlQueries(
        PostgreSqlPhysicalDocumentStore store,
        StorageManifest manifest,
        PhysicalSchemaTarget target) =>
        Route(
            target.Routes,
            route => PostgreSqlPhysicalQueryRuntime.Create(store, manifest, route, target.Provider));

    public static async Task<GroundworkPhysicalTestStore<SqlServerPhysicalDocumentStore>> OpenSqlServerAsync(
        string connectionString,
        StorageManifest manifest,
        ProviderIdentity provider,
        DocumentStoreAccess access,
        CancellationToken cancellationToken = default)
    {
        var store = await SqlServerDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            access,
            options: AutoApply,
            cancellationToken: cancellationToken);
        var target = PhysicalSchemaTargetCompiler.Compile(
            manifest,
            provider,
            SqlServerGroundworkCapabilities.PhysicalNames);
        return new GroundworkPhysicalTestStore<SqlServerPhysicalDocumentStore>(
            store,
            SqlServerQueries(store, manifest, target));
    }

    public static GroundworkBoundedDocumentStoreRouter SqlServerQueries(
        SqlServerPhysicalDocumentStore store,
        StorageManifest manifest,
        PhysicalSchemaTarget target) =>
        Route(
            target.Routes,
            route => SqlServerPhysicalQueryRuntime.Create(store, manifest, route, target.Provider));

    /// <summary>
    /// Binds one bounded runtime per declared storage unit. Doubles that answer every kind themselves still
    /// go through the router so terminal result operations are selected the way a provider host selects them.
    /// </summary>
    public static GroundworkBoundedDocumentStoreRouter Route(
        StorageManifest manifest,
        Func<StorageUnit, IBoundedDocumentStore> createRuntime)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(createRuntime);
        return new GroundworkBoundedDocumentStoreRouter(
            manifest.StorageUnits.Select(unit =>
                KeyValuePair.Create(unit.Identity.Value, createRuntime(unit))));
    }

    /// <summary>Binds one already-compiled per-route runtime per storage unit.</summary>
    public static GroundworkBoundedDocumentStoreRouter Route(
        IEnumerable<ExecutableStorageRoute> routes,
        Func<ExecutableStorageRoute, IBoundedDocumentStore> createRuntime)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(createRuntime);
        return new GroundworkBoundedDocumentStoreRouter(
            routes.Select(route => KeyValuePair.Create(route.StorageUnit.Value, createRuntime(route))));
    }
}

/// <summary>A physically-opened provider store paired with the bounded-query runtime bound to its routes.</summary>
public sealed record GroundworkPhysicalTestStore<TStore>(
    TStore Store,
    GroundworkBoundedDocumentStoreRouter Queries)
    where TStore : IDocumentStore;

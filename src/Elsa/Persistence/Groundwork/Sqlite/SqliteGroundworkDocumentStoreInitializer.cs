using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Transactions;
using ElsaAdmissionException = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionException;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Relational.Documents;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Admits the exact host-selected SQLite schema and then exposes one physical document store.
/// By default, runtime startup only inspects schema; enable <c>autoApplyOnStartup</c> to apply
/// safe pending operations automatically.
/// </summary>
/// <exception cref="SqliteGroundworkPersistenceException">
/// Thrown when SQLite provider infrastructure fails during initialization or while opening an access-bound
/// store session. The originating provider exception is preserved as the inner exception.
/// </exception>
public sealed class SqliteGroundworkDocumentStoreInitializer(
    string connectionString,
    bool autoApplyOnStartup,
    IServiceScopeFactory scopeFactory,
    GroundworkStoreSessionSource sessionSource,
    ILogger<SqliteGroundworkDocumentStoreInitializer> logger,
    GroundworkProviderCapabilityAdmission? capabilityAdmission = null) : IHostedService, IShellInitializer
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => EnsureInitializedAsync(cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken) => EnsureInitializedAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
                return;

            await using var scope = scopeFactory.CreateAsyncScope();
            var capabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
                SqliteGroundworkCapabilities.Runtime(),
                new GroundworkProviderTopologySnapshot(
                    SqliteGroundworkCapabilities.Provider.Name,
                    "sqlite-file",
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                    }),
                scope.ServiceProvider.GetServices<IGroundworkStorageManifestSource>(),
                cancellationToken);
            var source = await scope.ServiceProvider
                .GetRequiredService<GroundworkStorageCompositionFactory>()
                .CreateSourceAsync(
                    capabilities,
                    SqliteGroundworkCapabilities.PhysicalNames,
                    cancellationToken);

            // Route through Groundwork's connection factory so the WAL/synchronous/busy-timeout pragmas apply.
            // A bare SqliteConnection here would create (and forever admit) the database in rollback-journal
            // mode: journal_mode is a persistent per-database property, and this initializer's connections are
            // the first to touch the file.
            await using var inspectionConnection = SqliteConnectionFactory.Create(connectionString);
            var admission = await source.InspectRuntimeAdmissionAsync(
                new SqlitePhysicalSchemaExecutor(inspectionConnection),
                new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = autoApplyOnStartup },
                entry => logger.Log(
                    entry.Level == GroundworkRuntimeSchemaAdmissionLogLevel.Information
                        ? LogLevel.Information
                        : LogLevel.Warning,
                    "{AdmissionMessage}",
                    entry.Message),
                cancellationToken);
            if (!admission.IsReady)
                throw new ElsaAdmissionException(admission);

            if (!sessionSource.IsInitialized)
            {
                var manifest = source.CreateManifest();
                var routes = source.PhysicalTarget.Routes;
                var provider = source.PhysicalTarget.Provider;
                // Compile each route's connection-independent plan set at most once for the whole process.
                // The Lazy is captured by the admitted session factory, so it is shared across every session
                // open: unused routes never compile, and used routes compile exactly once. Each session then
                // only pays a cheap Bind against its own connection-bound store.
                var planSets = routes.ToDictionary(
                    route => route.StorageUnit.Value,
                    route => new Lazy<RelationalPhysicalQueryPlanSet>(
                        () => SqlitePhysicalQueryRuntime.CompilePlanSet(manifest, route, provider)),
                    StringComparer.Ordinal);
                if (sessionSource.TrySetAdmitted(async (access, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    SqliteConnection? connection = null;
                    try
                    {
                        // Every store session must open through the pragma factory: journal_mode=WAL persists
                        // per-database, but synchronous=NORMAL and busy_timeout are per-connection.
                        connection = SqliteConnectionFactory.Create(connectionString);
                        await connection.OpenAsync(ct);
                        var store = new SqlitePhysicalDocumentStore(
                            connection,
                            manifest,
                            routes,
                            access);
                        var boundedStore = GroundworkBoundedDocumentStoreRouter.CreateLazy(
                            routes.Select(route =>
                                KeyValuePair.Create<string, Func<IBoundedDocumentStore>>(
                                    route.StorageUnit.Value,
                                    () => planSets[route.StorageUnit.Value].Value.Bind(store))));
                        return new GroundworkStoreSessionResources(store, boundedStore, connection);
                    }
                    catch (OperationCanceledException)
                    {
                        await DisposeAfterCancellationAsync(connection);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw new SqliteGroundworkPersistenceException(
                            SqliteGroundworkPersistenceOperation.OpenSession,
                            await DisposeAfterFailureAsync(connection, exception));
                    }
                }, TransactionBoundary.CrossUnitAtomic))
                {
                    capabilityAdmission?.TrySet(capabilities);
                }
            }

            initialized = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ElsaAdmissionException)
        {
            throw;
        }
        catch (GroundworkStorageCompositionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SqliteGroundworkPersistenceException(
                SqliteGroundworkPersistenceOperation.Initialize,
                exception);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static async ValueTask<Exception> DisposeAfterFailureAsync(
        SqliteConnection? connection,
        Exception failure)
    {
        if (connection is null)
            return failure;

        try
        {
            await connection.DisposeAsync();
            return failure;
        }
        catch (Exception cleanupFailure)
        {
            return new AggregateException(
                "SQLite Groundwork session creation and cleanup both failed.",
                failure,
                cleanupFailure);
        }
    }

    private static async ValueTask DisposeAfterCancellationAsync(SqliteConnection? connection)
    {
        if (connection is null)
            return;

        try
        {
            await connection.DisposeAsync();
        }
        catch
        {
            // Preserve the operation's cancellation contract; cleanup cannot replace the requested cancellation.
        }
    }
}

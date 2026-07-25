using System.Diagnostics;
using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Primitives.Diagnostics;
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
    GroundworkProviderCapabilityAdmission? capabilityAdmission = null,
    bool skipInspectionWhenPlanUnchanged = false,
    SqliteGroundworkStoreCacheOptions? storeCacheOptions = null) : IHostedService, IShellInitializer
{
    private static readonly SqliteConnectionPragmaOptions OperationConnectionPragmas =
        SqliteConnectionPragmaOptions.Default with { WriteAheadLogging = false };
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly SqliteGroundworkAdmissionStampStore stampStore = new();
    private readonly SqliteGroundworkStoreCacheOptions storeCacheOptions =
        ValidateStoreCacheOptions(storeCacheOptions);
    private volatile bool initialized;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => EnsureInitializedAsync(cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken) => EnsureInitializedAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = initialized
            ? SqliteGroundworkTelemetry.HistoryHitOutcome
            : SqliteGroundworkTelemetry.MaterializedOutcome;
        using var activity = ObservationalTelemetryScope.Start(
            SqliteGroundworkTelemetry.GetActivitySource,
            SqliteGroundworkTelemetry.ActivityName);
        var lockTaken = false;

        try
        {
            if (initialized)
            {
                outcome = SqliteGroundworkTelemetry.HistoryHitOutcome;
                return;
            }

            await initializationLock.WaitAsync(cancellationToken);
            lockTaken = true;
            if (initialized)
            {
                outcome = SqliteGroundworkTelemetry.HistoryHitOutcome;
                return;
            }

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

            // Route through Groundwork's connection factory so the WAL/synchronous/busy-timeout pragmas apply
            // whenever admission actually touches the database. Keep the connection unopened here: a rejected
            // no-auto-apply admission must remain side-effect free and must not create an empty database file.
            // Successful admission opens this connection before operation sessions disable the persistent WAL
            // pragma, so the database is still admitted in WAL mode exactly once.
            await using var inspectionConnection = SqliteConnectionFactory.Create(connectionString);

            // Skip-if-current fast path (spec 133): a matching applied-plan stamp proves the composed plan
            // is byte-for-byte the last successfully admitted plan, so the full inspection/validation walk
            // (a re-read plus per-route PRAGMA re-validation of every storage unit) can be skipped for a
            // single indexed scalar read. Opt-in, because the stamp covers the plan but not live provider
            // state, so it cannot detect drift introduced out-of-band while the host was down.
            var currentStamp = GroundworkAdmissionSkipStamp.ForSource(source);
            var manifestId = source.PhysicalTarget.ManifestIdentity.Value;
            var providerName = source.PhysicalTarget.Provider.Name;

            var skipped = false;
            if (skipInspectionWhenPlanUnchanged)
            {
                var persistedStamp = await stampStore.TryReadAsync(
                    inspectionConnection, manifestId, providerName, cancellationToken);
                if (persistedStamp is not null && persistedStamp.Covers(currentStamp))
                {
                    skipped = true;
                    outcome = SqliteGroundworkTelemetry.HistoryHitOutcome;
                    logger.LogInformation(
                        "Groundwork runtime schema admission skipped the inspection walk for target '{TargetFingerprint}' on provider '{Provider}': the persisted applied-plan stamp is current.",
                        currentStamp.TargetFingerprint,
                        providerName);
                }
            }

            if (!skipped)
            {
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

                // Record the stamp only after the walk (and any apply) has durably committed and reported
                // ready. A crash before this line leaves no stamp, so the next boot re-walks and re-admits
                // idempotently — the stamp is an optimization, never a correctness gate. Only stamp when the
                // fast path is enabled; there is nothing to skip otherwise.
                if (skipInspectionWhenPlanUnchanged)
                    await stampStore.WriteAsync(
                        inspectionConnection, manifestId, providerName, currentStamp, cancellationToken);
            }

            // Groundwork's pragma-aware connection currently applies its options from synchronous Open().
            // Admission uses asynchronous provider I/O, so explicitly reopen the already-admitted target
            // through that override before operation stores suppress the persistent WAL pragma. This remains
            // after the readiness verdict so rejected no-auto-apply admission stays side-effect free.
            await inspectionConnection.CloseAsync();
            inspectionConnection.Open();

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
                GroundworkStoreSessionResources CreateResources(DocumentStoreAccess access)
                {
                    // Admission establishes WAL once. Reusable stores keep immutable compiled routes and
                    // access binding while each operation still owns an independent pooled connection.
                    var store = SqlitePhysicalDocumentStore.CreateConcurrent(
                        connectionString,
                        manifest,
                        source.PhysicalTarget,
                        access,
                        OperationConnectionPragmas);
                    var boundedStore = GroundworkBoundedDocumentStoreRouter.CreateLazy(
                        routes.Select(route =>
                            KeyValuePair.Create<string, Func<IBoundedDocumentStore>>(
                                route.StorageUnit.Value,
                                () => planSets[route.StorageUnit.Value].Value.Bind(store))));
                    return new GroundworkStoreSessionResources(store, boundedStore);
                }

                var stores = new AccessBoundGroundworkStoreCache<GroundworkStoreSessionResources>(
                    this.storeCacheOptions.Capacity,
                    CreateResources);
                Func<
                    DocumentStoreAccess,
                    CancellationToken,
                    ValueTask<GroundworkStoreSessionResources>> openSession =
                    this.storeCacheOptions.Enabled
                        ? (access, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            return ValueTask.FromResult(stores.GetOrCreate(access));
                        }
                        : OpenUncachedSessionAsync;
                if (sessionSource.TrySetAdmitted(openSession, TransactionBoundary.CrossUnitAtomic))
                {
                    capabilityAdmission?.TrySet(capabilities);
                }

                async ValueTask<GroundworkStoreSessionResources> OpenUncachedSessionAsync(
                    DocumentStoreAccess access,
                    CancellationToken ct)
                {
                    ct.ThrowIfCancellationRequested();
                    SqliteConnection? connection = null;
                    try
                    {
                        connection = SqliteConnectionFactory.Create(
                            connectionString,
                            OperationConnectionPragmas);
                        await connection.OpenAsync(ct);
                        var store = new SqlitePhysicalDocumentStore(
                            connection,
                            manifest,
                            source.PhysicalTarget,
                            access);
                        var boundedStore = GroundworkBoundedDocumentStoreRouter.CreateLazy(
                            routes.Select(route =>
                                KeyValuePair.Create<string, Func<IBoundedDocumentStore>>(
                                    route.StorageUnit.Value,
                                    () => planSets[route.StorageUnit.Value].Value.Bind(store))));
                        var boundedMutationStore = GroundworkBoundedDocumentMutationStoreRouter.CreateLazy(
                            routes
                                .Where(route => manifest.StorageUnits.Single(unit =>
                                    unit.Identity == route.StorageUnit).PhysicalStorage!.BoundedMutations.Count != 0)
                                .Select(route =>
                                    KeyValuePair.Create<string, Func<IBoundedDocumentMutationStore>>(
                                        route.StorageUnit.Value,
                                        () => SqlitePhysicalMutationRuntime.Create(store, manifest, route, provider))));
                        return new GroundworkStoreSessionResources(
                            store,
                            boundedStore,
                            boundedMutationStore,
                            connection);
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
                }
            }

            initialized = true;
        }
        catch (OperationCanceledException)
        {
            outcome = SqliteGroundworkTelemetry.CancelledOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (ElsaAdmissionException)
        {
            outcome = SqliteGroundworkTelemetry.FailedOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (GroundworkStorageCompositionException)
        {
            outcome = SqliteGroundworkTelemetry.FailedOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (Exception exception)
        {
            outcome = SqliteGroundworkTelemetry.FailedOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw new SqliteGroundworkPersistenceException(
                SqliteGroundworkPersistenceOperation.Initialize,
                exception);
        }
        finally
        {
            if (lockTaken)
                initializationLock.Release();

            activity.SetTag(SqliteGroundworkTelemetry.OutcomeTag, outcome);
            activity.Observe(
                SqliteGroundworkTelemetry.GetDuration,
                histogram => histogram.Record(
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    new KeyValuePair<string, object?>(SqliteGroundworkTelemetry.OutcomeTag, outcome)));
        }
    }

    private static SqliteGroundworkStoreCacheOptions ValidateStoreCacheOptions(
        SqliteGroundworkStoreCacheOptions? options)
    {
        options ??= new();
        options.Validate();
        return new()
        {
            Enabled = options.Enabled,
            Capacity = options.Capacity
        };
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

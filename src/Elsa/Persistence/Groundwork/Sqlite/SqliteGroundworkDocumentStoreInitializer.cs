using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Transactions;
using ElsaAdmissionException = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionException;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
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
public sealed class SqliteGroundworkDocumentStoreInitializer(
    string connectionString,
    bool autoApplyOnStartup,
    IServiceScopeFactory scopeFactory,
    GroundworkStoreSessionSource sessionSource,
    ILogger<SqliteGroundworkDocumentStoreInitializer> logger) : IHostedService, IShellInitializer
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

            await using var inspectionConnection = new SqliteConnection(connectionString);
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
                sessionSource.TrySetAdmitted(async (access, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var connection = new SqliteConnection(connectionString);
                    await connection.OpenAsync(ct);
                    var store = new SqlitePhysicalDocumentStore(
                        connection,
                        manifest,
                        source.PhysicalTarget.Routes,
                        access);
                    var boundedStore = new GroundworkBoundedDocumentStoreRouter(
                        source.PhysicalTarget.Routes.Select(route =>
                            KeyValuePair.Create<string, IBoundedDocumentStore>(
                                route.StorageUnit.Value,
                                SqlitePhysicalQueryRuntime.Create(
                                    store,
                                    manifest,
                                    route,
                                    source.PhysicalTarget.Provider))));
                    return new GroundworkStoreSessionResources(store, boundedStore, connection);
                }, TransactionBoundary.CrossUnitAtomic);
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
            throw SanitizedFailure(exception);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static InvalidOperationException SanitizedFailure(Exception exception) => new(
        $"SQLite Groundwork runtime initialization failed ({exception.GetType().Name}); provider and connection details were suppressed.");
}

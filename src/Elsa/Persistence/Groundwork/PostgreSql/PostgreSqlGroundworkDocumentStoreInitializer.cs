using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Transactions;
using ElsaAdmissionException = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionException;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Relational.Documents;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.Documents;
using Groundwork.PostgreSql.PhysicalStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.Groundwork.PostgreSql;

/// <summary>
/// Admits the exact host-selected PostgreSQL schema and then exposes one physical document store.
/// By default, runtime startup only inspects schema; enable <c>autoApplyOnStartup</c> to apply
/// safe pending operations automatically.
/// </summary>
public sealed class PostgreSqlGroundworkDocumentStoreInitializer(
    string targetName,
    string connectionString,
    bool autoApplyOnStartup,
    IServiceScopeFactory scopeFactory,
    GroundworkStoreSessionSource sessionSource,
    ILogger<PostgreSqlGroundworkDocumentStoreInitializer> logger,
    GroundworkProviderCapabilityAdmission? capabilityAdmission = null) : IGroundworkTargetAdmission
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public string TargetName { get; } = GroundworkTargetNames.Normalize(targetName);

    public Task AdmitAsync(CancellationToken cancellationToken = default) => EnsureInitializedAsync(cancellationToken);

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
                PostgreSqlGroundworkCapabilities.Runtime(),
                new GroundworkProviderTopologySnapshot(
                    PostgreSqlGroundworkCapabilities.Provider.Name,
                    "postgresql-server",
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
                    PostgreSqlGroundworkCapabilities.PhysicalNames,
                    cancellationToken);

            var admission = await source.InspectRuntimeAdmissionAsync(
                new PostgreSqlPhysicalSchemaExecutor(connectionString),
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
                        () => PostgreSqlPhysicalQueryRuntime.CompilePlanSet(manifest, route, provider)),
                    StringComparer.Ordinal);
                if (sessionSource.TrySetAdmitted((access, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var store = new PostgreSqlPhysicalDocumentStore(
                        connectionString,
                        manifest,
                        routes,
                        access);
                    var boundedStore = GroundworkBoundedDocumentStoreRouter.CreateLazy(
                        routes.Select(route =>
                            KeyValuePair.Create<string, Func<IBoundedDocumentStore>>(
                                route.StorageUnit.Value,
                                () => planSets[route.StorageUnit.Value].Value.Bind(store))));
                    return ValueTask.FromResult(new GroundworkStoreSessionResources(store, boundedStore));
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
            throw SanitizedFailure(exception);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static InvalidOperationException SanitizedFailure(Exception exception) => new(
        $"PostgreSQL Groundwork runtime initialization failed ({exception.GetType().Name}); provider and connection details were suppressed.");
}

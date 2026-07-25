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
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Groundwork.SqlServer.PhysicalStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.Groundwork.SqlServer;

/// <summary>
/// Admits one SQL Server physical target before exposing its document store. By default, runtime
/// startup only inspects schema; enable <c>autoApplyOnStartup</c> to apply safe pending operations
/// automatically.
/// </summary>
public sealed class SqlServerGroundworkDocumentStoreInitializer : IHostedService, IShellInitializer
{
    private readonly string _connectionString;
    private readonly bool _autoApplyOnStartup;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GroundworkStoreSessionSource _sessionSource;
    private readonly ILogger<SqlServerGroundworkDocumentStoreInitializer> _logger;
    private readonly GroundworkProviderCapabilityAdmission? _capabilityAdmission;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    internal SqlServerGroundworkDocumentStoreInitializer(
        string connectionString,
        bool autoApplyOnStartup,
        IServiceScopeFactory scopeFactory,
        GroundworkStoreSessionSource sessionSource,
        ILogger<SqlServerGroundworkDocumentStoreInitializer> logger,
        GroundworkProviderCapabilityAdmission? capabilityAdmission = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        _autoApplyOnStartup = autoApplyOnStartup;
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _sessionSource = sessionSource ?? throw new ArgumentNullException(nameof(sessionSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _capabilityAdmission = capabilityAdmission;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => EnsureInitializedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            var composition = await CreateSchemaSourceAsync(cancellationToken);
            var schemaSource = composition.Source;
            var admission = await schemaSource.InspectRuntimeAdmissionAsync(
                new SqlServerPhysicalSchemaExecutor(_connectionString),
                new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = _autoApplyOnStartup },
                entry => _logger.Log(
                    entry.Level == GroundworkRuntimeSchemaAdmissionLogLevel.Information
                        ? LogLevel.Information
                        : LogLevel.Warning,
                    "{AdmissionMessage}",
                    entry.Message),
                cancellationToken);
            if (!admission.IsReady)
                throw new ElsaAdmissionException(admission);

            if (!_sessionSource.IsInitialized)
            {
                var manifest = schemaSource.CreateManifest();
                var routes = admission.PhysicalTarget.Routes;
                var provider = admission.PhysicalTarget.Provider;
                // Compile each route's connection-independent plan set at most once for the whole process.
                // The Lazy is captured by the admitted session factory, so it is shared across every session
                // open: unused routes never compile, and used routes compile exactly once. Each session then
                // only pays a cheap Bind against its own connection-bound store.
                var planSets = routes.ToDictionary(
                    route => route.StorageUnit.Value,
                    route => new Lazy<RelationalPhysicalQueryPlanSet>(
                        () => SqlServerPhysicalQueryRuntime.CompilePlanSet(manifest, route, provider)),
                    StringComparer.Ordinal);
                if (_sessionSource.TrySetAdmitted((access, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var store = new SqlServerPhysicalDocumentStore(
                        _connectionString,
                        manifest,
                        routes,
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
                                    () => SqlServerPhysicalMutationRuntime.Create(store, manifest, route, provider))));
                    return ValueTask.FromResult(new GroundworkStoreSessionResources(
                        store,
                        boundedStore,
                        boundedMutationStore));
                }, TransactionBoundary.CrossUnitAtomic))
                {
                    _capabilityAdmission?.TrySet(composition.Capabilities);
                }
            }

            _initialized = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ElsaAdmissionException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(
                "SQL Server Groundwork startup admission failed; provider diagnostics were suppressed and no document store was exposed.");
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async ValueTask<GroundworkProviderSchemaSource> CreateSchemaSourceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var compositionFactory = scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionFactory>();
        var provider = SqlServerGroundworkCapabilities.Runtime();
        var capabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
            provider,
            new GroundworkProviderTopologySnapshot(
                provider.Provider.Name,
                "sqlserver",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            scope.ServiceProvider.GetServices<IGroundworkStorageManifestSource>(),
            cancellationToken);
        var source = await compositionFactory.CreateSourceAsync(
            capabilities,
            SqlServerGroundworkCapabilities.PhysicalNames,
            cancellationToken);
        return new GroundworkProviderSchemaSource(source, capabilities);
    }

    private sealed record GroundworkProviderSchemaSource(
        GroundworkPhysicalSchemaManifestSource Source,
        GroundworkProviderCapabilitySnapshot Capabilities);
}

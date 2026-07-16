using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.SchemaEvolution;
using ElsaAdmissionException = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionException;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
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
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    internal SqlServerGroundworkDocumentStoreInitializer(
        string connectionString,
        bool autoApplyOnStartup,
        IServiceScopeFactory scopeFactory,
        GroundworkStoreSessionSource sessionSource,
        ILogger<SqlServerGroundworkDocumentStoreInitializer> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        _autoApplyOnStartup = autoApplyOnStartup;
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _sessionSource = sessionSource ?? throw new ArgumentNullException(nameof(sessionSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            var schemaSource = await CreateSchemaSourceAsync(cancellationToken);
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

            var historyRoute = admission.PhysicalTarget.Routes.SingleOrDefault(route =>
                string.Equals(
                    route.StorageUnit.Value,
                    ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                    StringComparison.Ordinal));
            if (historyRoute is not null)
            {
                await using var queryScope = _scopeFactory.CreateAsyncScope();
                var historyQuery = queryScope.ServiceProvider.GetRequiredService<IGroundworkWorkflowExecutionStatePageQuery>();
                historyQuery.Bind(historyRoute);
                await historyQuery.PrepareAsync(cancellationToken);
            }
            if (!_sessionSource.IsInitialized)
            {
                var manifest = schemaSource.CreateManifest();
                _sessionSource.TrySet((access, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var store = new SqlServerPhysicalDocumentStore(
                        _connectionString,
                        manifest,
                        admission.PhysicalTarget.Routes,
                        access);
                    var boundedStore = new GroundworkBoundedDocumentStoreRouter(
                        admission.PhysicalTarget.Routes.Select(route =>
                            KeyValuePair.Create<string, IBoundedDocumentStore>(
                                route.StorageUnit.Value,
                                SqlServerPhysicalQueryRuntime.Create(
                                    store,
                                    manifest,
                                    route,
                                    admission.PhysicalTarget.Provider))));
                    return ValueTask.FromResult(new GroundworkStoreSessionResources(store, boundedStore));
                });
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

    private async ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSchemaSourceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var compositionFactory = scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionFactory>();
        var provider = SqlServerGroundworkCapabilities.Runtime();
        var capabilities = GroundworkProviderCapabilitySnapshot.ForFeatureRoutes(
            provider,
            new GroundworkProviderTopologySnapshot(
                provider.Provider.Name,
                "sqlserver",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            RuntimeGroundworkStorageManifestSource.FeatureName,
            [RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement()]);
        return await compositionFactory.CreateSourceAsync(
            capabilities,
            SqlServerGroundworkCapabilities.PhysicalNames,
            cancellationToken);
    }
}

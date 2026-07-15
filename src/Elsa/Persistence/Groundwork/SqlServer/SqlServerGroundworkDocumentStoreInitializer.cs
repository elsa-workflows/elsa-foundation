using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Groundwork.SqlServer.PhysicalStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.SqlServer;

/// <summary>
/// Admits one already-applied SQL Server physical target before exposing its document store.
/// Runtime startup performs inspection only; deployment tooling owns every schema mutation.
/// </summary>
public sealed class SqlServerGroundworkDocumentStoreInitializer : IHostedService, IShellInitializer
{
    private readonly string _connectionString;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly GroundworkDocumentStoreHolder _holder;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    internal SqlServerGroundworkDocumentStoreInitializer(
        string connectionString,
        IServiceScopeFactory scopeFactory,
        IServiceProvider serviceProvider,
        GroundworkDocumentStoreHolder holder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _holder = holder ?? throw new ArgumentNullException(nameof(holder));
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
            IPhysicalSchemaHistoryInspector inspector = new SqlServerPhysicalSchemaExecutor(_connectionString);
            var admission = await schemaSource.InspectRuntimeAdmissionAsync(inspector, cancellationToken);
            if (!admission.IsReady)
                throw AdmissionFailure(admission);

            var historyRoute = admission.PhysicalTarget.Routes.SingleOrDefault(route =>
                string.Equals(
                    route.StorageUnit.Value,
                    ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                    StringComparison.Ordinal));
            if (historyRoute is not null)
            {
                var historyQuery = _serviceProvider.GetRequiredService<IGroundworkWorkflowExecutionStatePageQuery>();
                historyQuery.Bind(historyRoute);
                await historyQuery.PrepareAsync(cancellationToken);
            }
            if (!_holder.IsInitialized)
            {
                var manifest = schemaSource.CreateManifest();
                var store = new SqlServerPhysicalDocumentStore(
                    _connectionString,
                    manifest,
                    admission.PhysicalTarget.Routes,
                    DocumentStoreAccess.Global);
                var boundedStore = new GroundworkBoundedDocumentStoreRouter(
                    admission.PhysicalTarget.Routes.Select(route =>
                        KeyValuePair.Create<string, IBoundedDocumentStore>(
                            route.StorageUnit.Value,
                            SqlServerPhysicalQueryRuntime.Create(
                                store,
                                manifest,
                                route,
                                admission.PhysicalTarget.Provider))));
                _holder.TrySet(store, boundedStore);
            }

            _initialized = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith("SQL Server Groundwork", StringComparison.Ordinal))
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
        var capabilities = new GroundworkProviderCapabilitySnapshot(
            provider,
            new GroundworkProviderTopologySnapshot(provider.Provider.Name, "sqlserver", new HashSet<string>()),
            []);
        return await compositionFactory.CreateSourceAsync(
            capabilities,
            SqlServerGroundworkCapabilities.PhysicalNames,
            cancellationToken);
    }

    private static InvalidOperationException AdmissionFailure(GroundworkRuntimeSchemaAdmissionResult admission)
    {
        var codes = admission.Diagnostics
            .Where(diagnostic => diagnostic.IsError)
            .Select(diagnostic => diagnostic.Code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var reason = codes.Length == 0 ? "not-ready" : string.Join(",", codes);
        return new InvalidOperationException(
            $"SQL Server Groundwork target '{admission.TargetFingerprint}' was not admitted ({reason}); no schema changes were attempted and no document store was exposed.");
    }
}

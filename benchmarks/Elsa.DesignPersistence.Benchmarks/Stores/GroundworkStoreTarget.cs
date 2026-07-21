using CShells.Lifecycle;
using Elsa.DesignPersistence.Benchmarks.Harness;
using Elsa.DesignPersistence.Benchmarks.Workload;
using Elsa.Persistence.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.DesignPersistence.Benchmarks.Stores;

/// <summary>The Groundwork SQLite unified store, composed through the real production registrations.
/// This is the physical-entity production form; its scale-bearing read behavior is the entity-form
/// number the EF-ratio gate compares against.</summary>
public sealed class GroundworkStoreTarget : IBenchmarkTarget
{
    private readonly DesignWorkload _workload;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-bench-gw-{Guid.NewGuid():N}");
    private ServiceProvider _services = null!;
    private CancellationTokenSource? _backgroundCancellation;
    private IReadOnlyList<IBackgroundTask> _backgroundTasks = [];
    private Task[] _backgroundExecutions = [];

    public GroundworkStoreTarget(DesignWorkload workload) => _workload = workload;

    public string Key => "groundwork.store";

    private string ConnectionString => $"Data Source={Path.Combine(_directory, "design.db")}";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        await DesignComposition.GroundworkSchemaCli.ApplyFreshAsync(ConnectionString, cancellationToken);

        _services = DesignComposition.BuildGroundwork(ConnectionString);
        foreach (var initializer in _services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(cancellationToken);

        _backgroundCancellation = new CancellationTokenSource();
        _backgroundTasks = _services.GetServices<IBackgroundTask>().ToArray();
        foreach (var task in _backgroundTasks)
            await task.StartAsync(_backgroundCancellation.Token);
        _backgroundExecutions = _backgroundTasks.Select(task => task.ExecuteAsync(_backgroundCancellation.Token)).ToArray();

        await DesignStoreOperations.SeedAsync(CreateScope, _workload, cancellationToken);
    }

    public IReadOnlyList<BenchmarkOperation> Operations => DesignStoreOperations.Build(CreateScope, _workload);

    public Task<IReadOnlyList<QueryPlanEvidence>> CaptureQueryPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QueryPlanEvidence>>([]);

    private IServiceScope CreateScope()
    {
        var scope = _services.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(DesignWorkload.Scope)));
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_backgroundCancellation is not null)
        {
            try
            {
                foreach (var task in _backgroundTasks)
                    await task.StopAsync(CancellationToken.None);
                await Task.WhenAll(_backgroundExecutions);
            }
            catch
            {
                // Best-effort background drain on teardown.
            }
            finally
            {
                await _backgroundCancellation.CancelAsync();
                _backgroundCancellation.Dispose();
            }
        }

        if (_services is not null)
            await _services.DisposeAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named directory is harmless if SQLite releases a sidecar late.
        }
    }
}

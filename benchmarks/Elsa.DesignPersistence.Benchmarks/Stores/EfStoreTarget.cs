using Elsa.DesignPersistence.Benchmarks.Harness;
using Elsa.DesignPersistence.Benchmarks.Workload;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.DesignPersistence.Benchmarks.Stores;

/// <summary>The temporary same-provider EF oracle (SQLite), composed through the real EF design
/// persistence registrations.</summary>
public sealed class EfStoreTarget : IBenchmarkTarget
{
    private readonly DesignWorkload _workload;
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"elsa-bench-ef-{Guid.NewGuid():N}.db");
    private ServiceProvider _services = null!;

    public EfStoreTarget(DesignWorkload workload) => _workload = workload;

    public string Key => "ef.normalized";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _services = DesignComposition.BuildEf($"Data Source={_databasePath}");
        await DesignComposition.MigrateEfAsync(_services, cancellationToken);
        await DesignStoreOperations.SeedAsync(() => _services.CreateScope(), _workload, cancellationToken);
    }

    public IReadOnlyList<BenchmarkOperation> Operations => DesignStoreOperations.Build(() => _services.CreateScope(), _workload);

    public Task<IReadOnlyList<QueryPlanEvidence>> CaptureQueryPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QueryPlanEvidence>>([]);

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A uniquely named SQLite file is harmless if a sidecar is released late.
        }
    }
}

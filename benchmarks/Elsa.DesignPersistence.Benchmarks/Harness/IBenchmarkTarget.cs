namespace Elsa.DesignPersistence.Benchmarks.Harness;

/// <summary>
/// One measurable adapter: a same-provider EF oracle, the Groundwork physical-entity store, or a
/// modeled Groundwork physical form (shared/linked, dedicated-document, physical-entity). A target
/// seeds the fixed workload once (untimed) and exposes the Benchmark Acceptance Catalog operations.
/// </summary>
public interface IBenchmarkTarget : IAsyncDisposable
{
    /// <summary>Adapter+form identity written into evidence, e.g. <c>groundwork.entity</c>.</summary>
    string Key { get; }

    /// <summary>Materializes the fixed dataset. Untimed warm-up work.</summary>
    Task SeedAsync(CancellationToken cancellationToken = default);

    /// <summary>The catalog operations this target measures.</summary>
    IReadOnlyList<BenchmarkOperation> Operations { get; }

    /// <summary>Captures provider-native plans for the exercised scale-bearing routes, if available.</summary>
    Task<IReadOnlyList<QueryPlanEvidence>> CaptureQueryPlansAsync(CancellationToken cancellationToken = default);
}

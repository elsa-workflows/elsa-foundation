using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Everything a leaf needs to bind a frozen workload to a real provider.</summary>
internal sealed record AdapterContext(RunRequest Request, PerformanceWorkload Workload);

/// <summary>
/// Maps a frozen workload id to the leaf that can execute it against a real provider.
///
/// An unregistered workload is a hard failure, never a fallback. The harness's own rule — "a missing
/// adapter is a blocked run, never a simulated result" — is only meaningful if the host refuses to
/// improvise, so the error names the gap instead of degrading to something measurable-looking.
/// </summary>
internal static class BenchmarkAdapterFactory
{
    private static readonly IReadOnlyDictionary<string, Func<AdapterContext, CancellationToken, ValueTask<IBenchmarkAdapter>>> Leaves =
        new Dictionary<string, Func<AdapterContext, CancellationToken, ValueTask<IBenchmarkAdapter>>>(StringComparer.Ordinal)
        {
            // Keyed by workload id alone, so one entry covers every provider and physical form; the leaf
            // itself validates the requested provider against the frozen topology contract.
            [RuntimeCheckpointCommitWorkload.WorkloadId] = CheckpointCommitAdapter.CreateAsync,
            [DiagnosticsDurableHistoryWorkload.WorkloadId] = DiagnosticsDurableHistoryAdapter.CreateAsync
        };

    public static IReadOnlyCollection<string> RegisteredWorkloads => Leaves.Keys.Order(StringComparer.Ordinal).ToArray();

    public static ValueTask<IBenchmarkAdapter> CreateAsync(AdapterContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Leaves.TryGetValue(context.Workload.Id, out var create))
            return create(context, cancellationToken);
        var registered = RegisteredWorkloads.Count == 0 ? "none" : string.Join(", ", RegisteredWorkloads);
        throw new PerformanceContractException(
            $"No adapter leaf is registered for workload '{context.Workload.Id}'. Registered leaves: {registered}. " +
            "A missing adapter is a blocked run, not a simulated result.");
    }
}

using System.Threading;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// ADR 0047 D3 benchmark-attribution counter. Counts how many times a per-node routing structure is actually
/// materialized (deserialized + reconstructed from <see cref="ExecutableNode.Structure"/>) via
/// <see cref="ExecutableNode.GetOrAddRoutingStructure{T}"/>, as opposed to being served from the in-memory memo.
///
/// It exists so the engine benchmarks can attribute the D3 win: before D3 the composite engines rebuilt the
/// routing graph on every completion hop (once per <see cref="ExecutableNode"/> Start + once per child completion);
/// with the memo riding on the spec-111 burst-cached executable instance, the structure materializes once per
/// composite per burst. Counting only the actual factory execution (never a memo hit) makes the collapse a
/// deterministic, reportable number — the routing analog of the existing commits/run and executable-reads/run
/// counters.
///
/// This is diagnostics only: it changes no behavior. The increment fires on a rare path (an actual materialization,
/// not a cache hit), so the global <see cref="Interlocked.Increment(ref long)"/> is not a hot-path cost. Nothing
/// in the runtime reads the count; only benchmarks call <see cref="Reset"/>/<see cref="Count"/>.
/// </summary>
public static class RoutingStructureMaterializationDiagnostics
{
    private static long _count;

    /// <summary>The number of routing-structure materializations observed since the last <see cref="Reset"/>.</summary>
    public static long Count => Interlocked.Read(ref _count);

    /// <summary>Resets the counter. Benchmarks call this immediately before a measured run.</summary>
    public static void Reset() => Interlocked.Exchange(ref _count, 0);

    internal static void OnMaterialized() => Interlocked.Increment(ref _count);
}

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The effective checkpoint cadence a run executes under (ADR 0032 R5), after applying the precedence rule:
/// per-workflow authored cadence over host default, clamped to what the host can actually reach. The mandatory-boundary
/// set is never expressed here — it overrides any cadence at the commit layer and can never be relaxed by configuration.
/// </summary>
/// <param name="Coalesced">Whether relaxable checkpoints coalesce (<see langword="true"/>) or flush immediately.</param>
/// <param name="MaxSegmentCheckpoints">The bounded segment cap when <paramref name="Coalesced"/>; otherwise <see langword="null"/>.</param>
public sealed record ResolvedCheckpointCadence(bool Coalesced, int? MaxSegmentCheckpoints)
{
    /// <summary>Every relaxable checkpoint flushes immediately — the default, narrowest replay window.</summary>
    public static ResolvedCheckpointCadence Immediate { get; } = new(false, null);

    /// <summary>Coalesced persistence bounded by <paramref name="maxSegmentCheckpoints"/>.</summary>
    public static ResolvedCheckpointCadence CoalescedWith(int maxSegmentCheckpoints) => new(true, maxSegmentCheckpoints);
}

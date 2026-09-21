namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// The effective checkpoint-persistence cadence of the host and the activity-execution inspection granularity it
/// implies, projected onto the instance read model (ADR 0032 R3). Under <see cref="CheckpointPersistenceMode.Coalesced"/>
/// persistence, intra-segment inspection projections fold last-writer-per-state between durable boundaries, so
/// per-activity inspection evidence becomes boundary-level rather than activity-level. Surfacing that here makes the
/// coarsening an explicit, configured property of the response rather than a silent loss the inspection UI must guess at.
/// </summary>
/// <param name="CheckpointCadence">
/// The active cadence, <c>"Immediate"</c> or <c>"Coalesced"</c> (the names of <see cref="CheckpointPersistenceMode"/>).
/// </param>
/// <param name="MaxSegmentCheckpoints">
/// The bounded number of replayable checkpoints a coalesced segment folds before an intermediate flush; <c>null</c>
/// under Immediate cadence, where every checkpoint persists as soon as it is decided.
/// </param>
/// <param name="InspectionGranularity">
/// <c>"activity-level"</c> under Immediate cadence (every activity's inspection evidence is exact) or
/// <c>"boundary-level"</c> under Coalesced cadence (evidence is folded to the segment's durable boundary).
/// </param>
public sealed record RuntimeCheckpointCadenceProjection(
    string CheckpointCadence,
    int? MaxSegmentCheckpoints,
    string InspectionGranularity)
{
    /// <summary>Inspection granularity when every activity's checkpoint persists exactly.</summary>
    public const string ActivityLevelGranularity = "activity-level";

    /// <summary>Inspection granularity when intra-segment evidence folds to the coalesced durable boundary.</summary>
    public const string BoundaryLevelGranularity = "boundary-level";

    /// <summary>The default cadence: each checkpoint persists immediately, so inspection stays activity-level.</summary>
    public static RuntimeCheckpointCadenceProjection Immediate { get; } =
        new(CheckpointPersistenceMode.Immediate.ToString(), null, ActivityLevelGranularity);

    /// <summary>The coalesced cadence bounded by <paramref name="maxSegmentCheckpoints"/>; inspection is boundary-level.</summary>
    public static RuntimeCheckpointCadenceProjection Coalesced(int maxSegmentCheckpoints) =>
        new(CheckpointPersistenceMode.Coalesced.ToString(), maxSegmentCheckpoints, BoundaryLevelGranularity);
}

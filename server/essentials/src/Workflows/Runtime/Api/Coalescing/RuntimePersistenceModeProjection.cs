using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// The persistence mode a run used and the activity-execution inspection granularity it implies, projected onto the
/// instance read model. When checkpoints fold between commits, intra-segment inspection projections fold
/// last-writer-per-state, so per-activity inspection evidence becomes boundary-level rather than activity-level.
/// Surfacing that here makes the coarsening an explicit property of the response rather than a silent loss the
/// inspection UI must guess at.
/// </summary>
/// <param name="PersistenceMode">The name of the run's <see cref="Core.Models.PersistenceMode"/>.</param>
/// <param name="InspectionGranularity">
/// <c>"activity-level"</c> under Durable (every activity's inspection evidence is exact) or <c>"boundary-level"</c>
/// when checkpoints fold (evidence is folded to the segment's durable boundary).
/// </param>
public sealed record RuntimePersistenceModeProjection(
    string PersistenceMode,
    string InspectionGranularity)
{
    /// <summary>Inspection granularity when every activity's checkpoint persists exactly.</summary>
    public const string ActivityLevelGranularity = "activity-level";

    /// <summary>Inspection granularity when intra-segment evidence folds to the durable boundary.</summary>
    public const string BoundaryLevelGranularity = "boundary-level";

    /// <summary>Projects <paramref name="mode"/> and the inspection granularity it implies.</summary>
    public static RuntimePersistenceModeProjection For(Core.Models.PersistenceMode mode) =>
        new(mode.ToString(), mode == Core.Models.PersistenceMode.Durable ? ActivityLevelGranularity : BoundaryLevelGranularity);
}

using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Resolves the effective checkpoint-persistence cadence for projection onto instance read models (ADR 0032 R3/R5).
/// Prefers the run's own per-run cadence stamp on <see cref="WorkflowExecutionState.SystemMetadata"/>
/// (<see cref="RuntimeMetadataKeys.CheckpointCadence"/>), written at start from the pinned executable's authored cadence
/// over the host default (WU-config surface). Falls back to the host projection for instances predating the stamp: the
/// registered <see cref="CoalescingRuntimeCheckpointPersistenceOptions"/> are present only under Coalesced mode
/// (<see cref="CoalescingRuntimeCheckpointPersistenceExtensions.AddCoalescingRuntimeCheckpointPersistence"/>), so their
/// presence is the host cadence signal and their absence means the default Immediate policy.
/// </summary>
/// <remarks>
/// With the per-run stamp, an instance reports the cadence it <em>actually executed under</em>, so reconfiguring the
/// host default no longer retroactively changes what an older instance reports. Only unstamped legacy instances are
/// reported under the host's current setting.
/// </remarks>
public sealed class RuntimeCheckpointCadenceInspector(IEnumerable<CoalescingRuntimeCheckpointPersistenceOptions> coalescingOptions)
{
    private readonly CoalescingRuntimeCheckpointPersistenceOptions? _options = coalescingOptions.SingleOrDefault();

    /// <summary>Projects the host's effective cadence and the inspection granularity it implies (fallback / host default).</summary>
    public RuntimeCheckpointCadenceProjection Resolve() =>
        _options is null
            ? RuntimeCheckpointCadenceProjection.Immediate
            : RuntimeCheckpointCadenceProjection.Coalesced(_options.MaxSegmentCheckpoints);

    /// <summary>
    /// Projects the cadence a specific run executed under, preferring its per-run stamp and falling back to the host
    /// projection for instances that predate it.
    /// </summary>
    public RuntimeCheckpointCadenceProjection Resolve(WorkflowExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.CheckpointCadence, out var mode))
            return Resolve();

        if (StringComparer.Ordinal.Equals(mode, WorkflowExecutableCheckpointCadence.CoalescedMode) &&
            state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints, out var maxRaw) &&
            int.TryParse(maxRaw, out var max) && max > 0)
            return RuntimeCheckpointCadenceProjection.Coalesced(max);

        return RuntimeCheckpointCadenceProjection.Immediate;
    }
}

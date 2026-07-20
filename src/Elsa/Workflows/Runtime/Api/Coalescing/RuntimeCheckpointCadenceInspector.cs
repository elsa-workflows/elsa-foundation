using Elsa.Workflows.Runtime.Core.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Resolves the host's effective checkpoint-persistence cadence for projection onto instance read models (ADR 0032 R3).
/// The cadence is a host-configuration property, not per-instance state, so it is derived at view-assembly time from the
/// registered coalescing options: <see cref="CoalescingRuntimeCheckpointPersistenceExtensions.AddCoalescingRuntimeCheckpointPersistence"/>
/// registers <see cref="CoalescingRuntimeCheckpointPersistenceOptions"/> only when Coalesced mode is selected, so their
/// presence is the cadence signal and their absence means the default Immediate policy.
/// </summary>
/// <remarks>
/// Limitation: this reports the host's <em>currently-configured</em> cadence, not the cadence a given run actually
/// executed under. The runtime records no per-run cadence stamp on <c>WorkflowExecutionState</c> (checkpoint/commit
/// metadata such as the coalesced-flush marker lives on transient checkpoints, not the durable instance), so an
/// instance that ran before the host's cadence was reconfigured is reported under the current setting. When a cheap
/// per-run source of truth is added — ADR 0032 notes cadence can become per-workflow via the WU-config surface — this
/// inspector should prefer it over the host projection.
/// </remarks>
public sealed class RuntimeCheckpointCadenceInspector(IEnumerable<CoalescingRuntimeCheckpointPersistenceOptions> coalescingOptions)
{
    private readonly CoalescingRuntimeCheckpointPersistenceOptions? _options = coalescingOptions.SingleOrDefault();

    /// <summary>Projects the host's effective cadence and the inspection granularity it implies.</summary>
    public RuntimeCheckpointCadenceProjection Resolve() =>
        _options is null
            ? RuntimeCheckpointCadenceProjection.Immediate
            : RuntimeCheckpointCadenceProjection.Coalesced(_options.MaxSegmentCheckpoints);
}

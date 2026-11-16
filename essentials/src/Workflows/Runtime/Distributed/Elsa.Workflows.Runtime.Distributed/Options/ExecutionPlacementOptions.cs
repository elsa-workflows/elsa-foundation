namespace Elsa.Workflows.Runtime.Distributed.Options;

/// <summary>
/// Options for the distributed workflow-execution actor provider's placement and transport coordination. All time
/// windows are consumed through an injected <see cref="TimeProvider"/> so the placement pump and the two-node tests
/// advance a controllable clock rather than sleeping on wall time.
/// </summary>
public sealed class ExecutionPlacementOptions
{
    /// <summary>
    /// Stable identity of this host/process as a placement owner. Distinct per node; written onto placement leases and
    /// transport lease holders so a surviving node can distinguish its own claims from a peer's. Defaults to a
    /// machine + process identity stable for the lifetime of the process. It must satisfy
    /// <see cref="Elsa.Workflows.Runtime.Distributed.Contracts.DistributedRuntimeIdentityConstraints"/>.
    /// </summary>
    public string NodeId { get; set; } = $"node:{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>
    /// How long a placement claim is valid before it must be renewed. A node that dies stops renewing; once the lease
    /// expires a surviving node may claim placement and re-drive the execution. Also used as the transport dequeue
    /// visibility window so a command leased by a dead node becomes re-deliverable.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
}

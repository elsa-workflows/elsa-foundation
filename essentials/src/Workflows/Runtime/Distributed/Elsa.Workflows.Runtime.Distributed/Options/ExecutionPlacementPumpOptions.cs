namespace Elsa.Workflows.Runtime.Distributed.Options;

/// <summary>
/// Tunables for the placement pump. Cadence and bounds are expressed as options and evaluated against the injected
/// <see cref="TimeProvider"/> so there are no wall-clock literals in the pump; the two-node test drives a controllable
/// time provider only. Lease TTL itself lives on <see cref="ExecutionPlacementOptions.LeaseDuration"/>; this object
/// governs how often the pump renews held leases and re-drives transport backlog.
/// </summary>
public sealed class ExecutionPlacementPumpOptions
{
    private int _maxExecutionsPerSweep = 100;
    private int _transportLeaseBatchSize = 100;

    /// <summary>Baseline interval between placement sweeps while the node is healthy.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound the sweep interval backs off to after consecutive whole-sweep failures.</summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a sweep waits for one dispatched command, or for one execution's passivation or hand-off, before leaving
    /// it to finish on its own (#2831). A command starts a drain that may run for as long as its workflow keeps making
    /// progress. Waiting it out would stop the sweep from renewing this node's placements, and from delivering a Cancel
    /// to the drain's mailbox. A command that finishes in time is acknowledged in the sweep; a later one is acknowledged
    /// when it finishes.
    /// </summary>
    /// <remarks>
    /// All of one sweep's waits share a budget of a third of <see cref="ExecutionPlacementOptions.LeaseDuration"/>, so a
    /// value at or above that budget waits no longer than the budget does. A Cancel for an execution whose drain the
    /// pump left running is not waited for at all.
    /// </remarks>
    public TimeSpan InlineDispatchWait { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Hard cap on executions claimed and re-driven per sweep, bounding dispatch bursts.</summary>
    public int MaxExecutionsPerSweep
    {
        get => _maxExecutionsPerSweep;
        set => _maxExecutionsPerSweep = Contracts.DistributedRuntimeQueryLimits.ValidateTake(
            value,
            nameof(MaxExecutionsPerSweep));
    }

    /// <summary>Maximum transport items leased per owned execution per sweep.</summary>
    public int TransportLeaseBatchSize
    {
        get => _transportLeaseBatchSize;
        set => _transportLeaseBatchSize = Contracts.DistributedRuntimeQueryLimits.ValidateTake(
            value,
            nameof(TransportLeaseBatchSize));
    }
}

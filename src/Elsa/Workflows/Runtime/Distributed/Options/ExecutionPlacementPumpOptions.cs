namespace Elsa.Workflows.Runtime.Distributed.Options;

/// <summary>
/// Tunables for the placement pump. Cadence and bounds are expressed as options and evaluated against the injected
/// <see cref="TimeProvider"/> so there are no wall-clock literals in the pump; the two-node test drives a controllable
/// time provider only. Lease TTL itself lives on <see cref="ExecutionPlacementOptions.LeaseDuration"/>; this object
/// governs how often the pump renews held leases and re-drives transport backlog.
/// </summary>
public sealed class ExecutionPlacementPumpOptions
{
    /// <summary>Baseline interval between placement sweeps while the node is healthy.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound the sweep interval backs off to after consecutive whole-sweep failures.</summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Hard cap on executions claimed and re-driven per sweep, bounding dispatch bursts.</summary>
    public int MaxExecutionsPerSweep { get; set; } = 100;

    /// <summary>Maximum transport items leased per owned execution per sweep.</summary>
    public int TransportLeaseBatchSize { get; set; } = 100;
}

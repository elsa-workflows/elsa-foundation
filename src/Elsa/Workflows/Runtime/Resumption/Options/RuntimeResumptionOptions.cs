namespace Elsa.Workflows.Runtime.Resumption.Options;

/// <summary>
/// Tunables for the runtime resumption pump. Bounds every sweep so a large backlog or a poisoned
/// execution cannot overwhelm the host: batch sizes and <see cref="MaxExecutionsPerSweep"/> cap the
/// work per tick, and the backoff settings govern both the whole-sweep retry interval and the
/// per-execution skip window after a failed re-drive.
/// </summary>
public sealed class RuntimeResumptionOptions
{
    /// <summary>Baseline interval between sweeps while the host is healthy.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Upper bound the sweep interval backs off to after consecutive whole-sweep failures. Also caps
    /// the per-execution failure backoff window.
    /// </summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum post-commit outbox items delivered per sweep.</summary>
    public int OutboxBatchSize { get; set; } = 100;

    /// <summary>Maximum stranded-backlog execution IDs discovered from the durable queue per sweep.</summary>
    public int BacklogBatchSize { get; set; } = 100;

    /// <summary>Maximum recovery-scanner candidates requested per sweep.</summary>
    public int RecoveryScanBatchSize { get; set; } = 100;

    /// <summary>Hard cap on executions re-driven per sweep after discovery, bounding dispatch bursts.</summary>
    public int MaxExecutionsPerSweep { get; set; } = 100;

    /// <summary>Lease-expiry threshold handed to the recovery scanner.</summary>
    public TimeSpan LeaseTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Heartbeat-expiry threshold handed to the recovery scanner.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

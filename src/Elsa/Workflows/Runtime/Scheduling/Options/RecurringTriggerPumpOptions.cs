namespace Elsa.Workflows.Runtime.Scheduling.Options;

/// <summary>
/// Tunables for the recurring-trigger pump. Every sweep is bounded so a large schedule set or a failing
/// schedule cannot overwhelm the host: <see cref="MaxSchedulesPerTick"/> caps the work per tick and the backoff
/// settings widen the whole-sweep retry interval after consecutive failures.
/// </summary>
public sealed class RecurringTriggerPumpOptions
{
    /// <summary>Baseline interval between sweeps while the host is healthy.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound the sweep interval backs off to after consecutive whole-sweep failures.</summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Hard cap on due schedules claimed and fired per sweep, bounding both the store scan and the start burst.</summary>
    public int MaxSchedulesPerTick { get; set; } = 100;
}

namespace Elsa.Workflows.Runtime.Scheduling.Options;

/// <summary>
/// Tunables for the durable timer pump. Every sweep is bounded so a large timer backlog or a poisoned
/// timer cannot overwhelm the host: <see cref="MaxTimersPerTick"/> caps the work per tick and the backoff
/// settings govern both the whole-sweep retry interval and the per-timer skip window after a failed fire.
/// </summary>
public sealed class DurableTimerPumpOptions
{
    /// <summary>Baseline interval between sweeps while the host is healthy.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Upper bound the sweep interval backs off to after consecutive whole-sweep failures. Also caps the
    /// per-timer failure backoff window.
    /// </summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Hard cap on due timers loaded and dispatched per sweep, bounding both the store scan and the
    /// dispatch burst.
    /// </summary>
    public int MaxTimersPerTick { get; set; } = 100;

    /// <summary>
    /// Visibility lease held while a claim-capable store dispatches one timer. The pump renews the lease
    /// while dispatch is in progress.
    /// </summary>
    public TimeSpan ClaimVisibilityTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Grace period after a timer's due time before a <c>NotFound</c> dispatch (no matching bookmark)
    /// deletes the timer. Within the grace window a <c>NotFound</c> is treated as "bookmark not yet
    /// committed" (a very short delay racing its own bookmark commit) and retried; past the grace window it
    /// is treated as "bookmark already consumed or gone" and the timer is deleted. This is what makes a
    /// duplicate/late fire converge instead of hanging.
    /// </summary>
    public TimeSpan NotFoundGrace { get; set; } = TimeSpan.FromSeconds(30);
}

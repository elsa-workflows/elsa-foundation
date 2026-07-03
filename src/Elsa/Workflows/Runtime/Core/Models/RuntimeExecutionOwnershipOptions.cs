namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Options for the runtime execution ownership service (single-writer fencing, RT-2).
/// </summary>
public sealed class RuntimeExecutionOwnershipOptions
{
    /// <summary>
    /// Stable identifier for this host/process as an execution owner. Written onto the lease and heartbeat so the
    /// recovery scanner's owner filter can attribute interrupted executions to a host. Defaults to a machine + process
    /// identity that is stable for the lifetime of the process.
    /// </summary>
    public string OwnerId { get; set; } = $"inproc:{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>
    /// How long an acquired lease is valid. The lease's <c>ExpiresAt</c> is set from this duration; the recovery
    /// scanner reads that expiry directly (via <c>RuntimeExecutionLease.IsExpired</c>), so this is the single lease
    /// lifetime knob rather than a parallel scanner threshold.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
}

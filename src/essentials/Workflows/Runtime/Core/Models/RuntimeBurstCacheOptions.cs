namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Kill switch for the burst-scoped reconstructible cache (ADR 0031 item b, spec 111). Follows the plain-options
/// idiom of <see cref="WorkflowDrainOrchestratorOptions"/> / <c>RuntimeExecutionOwnershipOptions</c> (this base ships
/// no <c>RuntimeInProcessHopFastPathOptions</c> to mirror). Registered as a singleton and read once per drain by
/// <c>WorkflowSchedulerCommandRouter</c>.
/// </summary>
/// <remarks>
/// When <see cref="Enabled"/> is <see langword="false"/> the router never establishes a burst scope, so every
/// drain-path executable read takes the durable path and the cache-off arm of the ADR guardrail is exercised. Because
/// the cached artifact is immutable and content-addressed (ADR 0038), toggling this option changes only read counts and
/// wall time — never committed durable state (the load-bearing "memory is cache, never a correctness dependency"
/// invariant).
/// </remarks>
public sealed class RuntimeBurstCacheOptions
{
    /// <summary>Whether the burst-scoped cache is active. Default <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;
}

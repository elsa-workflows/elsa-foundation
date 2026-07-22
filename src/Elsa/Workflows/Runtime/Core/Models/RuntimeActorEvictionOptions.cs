namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Controls in-process workflow-execution actor eviction (#542 / ADR 0031 follow-up — the burst work unit owns agent
/// lifetime/eviction). An idle mailbox is inert heap (two dictionary entries, two <see cref="SemaphoreSlim"/>, and a
/// bounded idempotency cache) with no timers or loops, but nothing released it before this unit, so the live agent
/// registry grew by one mailbox per execution ever activated.
///
/// <para><b>Terminal eviction only.</b> Eviction fires strictly when a dispatched command's drain committed a terminal
/// workflow status. A non-terminal (suspended) execution keeps its mailbox because its idempotency cache is still a live
/// dedup layer for redelivered resume/scheduler work; this is deliberately not an LRU/idle policy.</para>
///
/// <para><b>Kill switch.</b> The default (<see cref="PassivateOnTerminal"/> = <see langword="true"/>) evicts terminal
/// mailboxes at drain end. Setting it to <see langword="false"/> is byte-identical to the pre-#542 behavior — the
/// registry grows unboundedly again — and exists so the growth-vs-bounded contrast is A/B testable and a host has an
/// escape hatch. Eviction never runs inside a mailbox critical section and never touches a non-terminal execution, so
/// single-writer-per-execution (ADR 0031 ratification resolution #2) is preserved either way.</para>
/// </summary>
public sealed class RuntimeActorEvictionOptions
{
    /// <summary>
    /// When true (default), the provider passivates an execution's mailbox after a dispatched command's drain reports a
    /// terminal workflow status, bounding the live agent registry. When false, terminal mailboxes are retained (pre-#542
    /// unbounded-growth behavior).
    /// </summary>
    public bool PassivateOnTerminal { get; set; } = true;
}

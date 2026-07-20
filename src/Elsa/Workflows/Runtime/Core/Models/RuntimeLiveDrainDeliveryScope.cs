using System.Diagnostics.CodeAnalysis;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Marks that a live drain loop currently owns post-commit intent delivery for a single workflow execution
/// (WU-2, spec 105-runtime-live-drain-delivery). While this scope is ambient, the post-commit outbox processor
/// delivers <c>EnqueueSchedulerWork</c> intents for the owning execution IN-MEMORY: it enqueues the continuation
/// work item through the queue's idempotent <c>EnqueueAsync</c> and marks the durable outbox item Delivered directly,
/// WITHOUT the durable claim round-trip. Ownership is bounded by the drain's single-writer lease (RT-2), so no other
/// deliverer competes for the same execution's intents; the durable outbox item remains a crash backstop that the
/// resumption sweep re-drives idempotently.
///
/// <para><b>In-process-hop payload carrier (WU-3, spec 109, ADR 0031 follow-up item (a)).</b> The scope also carries the
/// already-materialized continuation <see cref="RuntimeSchedulerWorkItem"/>s across the enqueue→dispatch hop in memory,
/// keyed by post-commit intent id. The checkpoint committer publishes each continuation the moment its commit lands
/// (<see cref="PublishHopWorkItem"/>); the scheduler post-commit intent dispatcher takes it back
/// (<see cref="TryTakeHopWorkItem"/>) instead of deserializing the durable intent payload. The carrier is bounded by
/// this scope's lifetime — the drain orchestrator pushes it for the duration of one Immediate live drain and pops it at
/// drain end, discarding the carrier and every entry with it — so entries are never read after a crash, redrive, or
/// cross-drain hand-off. The durable outbox payload stays authoritative: if the carrier has no entry (fast path off,
/// coalescing active, sweep delivery, or a durable store that stripped the in-memory object) the dispatcher takes the
/// deserialize path and produces an identical work item. Memory is a cache; the serialized form is truth.</para>
/// </summary>
public sealed class RuntimeLiveDrainDeliveryScope
{
    // Single-writer-per-execution (ADR 0031 decision (b)): only the owning live drain publishes and takes on this
    // scope, so no lock is needed. A plain dictionary keeps the carrier allocation-light on the hot path.
    private readonly Dictionary<string, RuntimeSchedulerWorkItem> _hopWorkItems = new(StringComparer.Ordinal);

    public RuntimeLiveDrainDeliveryScope(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        WorkflowExecutionId = workflowExecutionId;
    }

    /// <summary>The workflow execution whose post-commit intents this live drain owns.</summary>
    public string WorkflowExecutionId { get; }

    /// <summary>True when this scope owns the given execution's intent delivery.</summary>
    public bool AppliesTo(string? workflowExecutionId) =>
        workflowExecutionId is not null &&
        StringComparer.Ordinal.Equals(WorkflowExecutionId, workflowExecutionId);

    /// <summary>
    /// Publishes the materialized continuation work item for a post-commit intent so the next in-process hop can skip
    /// deserializing the durable intent payload. Idempotent per intent id: a re-published intent (e.g. a multi-commit
    /// handler re-recording the same continuation) overwrites the entry with an equivalent work item.
    /// </summary>
    public void PublishHopWorkItem(string intentId, RuntimeSchedulerWorkItem workItem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentNullException.ThrowIfNull(workItem);
        _hopWorkItems[intentId] = workItem;
    }

    /// <summary>
    /// Takes (and removes) the materialized continuation work item for a post-commit intent, if one was published
    /// during this drain. Remove-on-read keeps the carrier bounded and guarantees a single in-process hand-off per
    /// intent; a re-delivered intent (idempotent enqueue dedupes it) falls through to the durable deserialize path.
    /// </summary>
    public bool TryTakeHopWorkItem(string intentId, [NotNullWhen(true)] out RuntimeSchedulerWorkItem? workItem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        if (_hopWorkItems.Remove(intentId, out var taken))
        {
            workItem = taken;
            return true;
        }

        workItem = null;
        return false;
    }
}

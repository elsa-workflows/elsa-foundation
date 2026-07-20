namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Marks that a live drain loop currently owns post-commit intent delivery for a single workflow execution
/// (WU-2, spec 105-runtime-live-drain-delivery). While this scope is ambient, the post-commit outbox processor
/// delivers <c>EnqueueSchedulerWork</c> intents for the owning execution IN-MEMORY: it enqueues the continuation
/// work item through the queue's idempotent <c>EnqueueAsync</c> and marks the durable outbox item Delivered directly,
/// WITHOUT the durable claim round-trip. Ownership is bounded by the drain's single-writer lease (RT-2), so no other
/// deliverer competes for the same execution's intents; the durable outbox item remains a crash backstop that the
/// resumption sweep re-drives idempotently.
/// </summary>
public sealed class RuntimeLiveDrainDeliveryScope
{
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
}

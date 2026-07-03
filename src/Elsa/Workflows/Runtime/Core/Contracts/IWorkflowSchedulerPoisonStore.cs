using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores <see cref="RuntimeSchedulerPoisonRecord"/>s for scheduler work handler crashes, keyed by workflow
/// execution ID and work item ID. Lets a handler crash be recorded (and, when the domain retry policy asks,
/// re-driven) instead of being silently dropped after dequeue (RT-1 gap b).
/// </summary>
public interface IWorkflowSchedulerPoisonStore
{
    /// <summary>Inserts or replaces the poison record for its <c>(WorkflowExecutionId, WorkItemId)</c> key.</summary>
    ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default);

    /// <summary>Returns the poison record for the given work item, or <see langword="null"/> when none exists.</summary>
    ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default);

    /// <summary>Returns all poison records for the given workflow execution.</summary>
    ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}

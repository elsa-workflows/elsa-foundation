using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores <see cref="RuntimeSchedulerPoisonRecord"/>s for scheduler work handler crashes, keyed by workflow
/// execution ID and work item ID. Lets a handler crash be recorded (and, when the domain retry policy asks,
/// re-driven) instead of being silently dropped after dequeue.
/// </summary>
/// <remarks>
/// A store that can check the run's ownership lease in the same unit of work as the write also implements
/// <see cref="IWorkflowSchedulerFencedPoisonStore"/>. The drainer then records through that capability.
/// </remarks>
public interface IWorkflowSchedulerPoisonStore
{
    /// <summary>
    /// Inserts or replaces the poison record for its <c>(WorkflowExecutionId, WorkItemId)</c> key. It makes no
    /// ownership check: a writer that no longer owns the workflow execution is not refused.
    /// </summary>
    ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default);

    /// <summary>Returns the poison record for the given work item, or <see langword="null"/> when none exists.</summary>
    ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default);

    /// <summary>Returns all poison records for the given workflow execution.</summary>
    ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}

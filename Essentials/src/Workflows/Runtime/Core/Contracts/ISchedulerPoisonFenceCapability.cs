using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional capability of an <see cref="IWorkflowSchedulerPoisonStore"/>: the store can write a poison record only
/// while the writer still owns the workflow execution, by checking the execution's ownership lease in the same unit
/// of work as the write.
/// </summary>
/// <remarks>
/// <para>
/// A drain that lost its run keeps running until one of its own writes is refused. If a work handler throws in that
/// time, its poison record would land on a run that another node now runs. The drainer checks the lease before it
/// touches the faulted step; this capability covers the time between that check and the record. A record written
/// through it cannot land on a run that changed owner in that time. It is not a guarantee for every record: when the
/// fenced write fails for a reason other than ownership, the drainer writes the record without the fence.
/// </para>
/// <para>
/// Implement it on the store object itself. The check and the write must be atomic: reading the lease first and
/// writing afterwards lets another node take the run in between. A store can do that only when the ownership state
/// (the <see cref="IExecutionLivenessStateStore"/> row named by <c>RuntimeExecutionOwnershipStateId</c>) lives in the
/// same backend as the poison records. The two are registered separately, so a host can combine a poison store of
/// one backend with ownership of another; <see cref="CanRecordFenced"/> is how the store says which case it is in.
/// </para>
/// <para>
/// A store without the capability, or one that reports <see cref="CanRecordFenced"/> as <see langword="false"/>, has
/// its records written through <see cref="IWorkflowSchedulerPoisonStore.RecordAsync"/> with no ownership check. A
/// decorator must implement this interface and forward both members to keep the check; a decorator that does not
/// leaves the record unfenced.
/// </para>
/// </remarks>
public interface ISchedulerPoisonFenceCapability
{
    /// <summary>
    /// Whether this store can check the ownership lease in the same unit of work as the record. It is
    /// <see langword="true"/> only when the registered ownership state lives in the store's own backend.
    /// </summary>
    bool CanRecordFenced { get; }

    /// <summary>
    /// Inserts or replaces the poison record for its <c>(WorkflowExecutionId, WorkItemId)</c> key, only if
    /// <paramref name="fence"/> is the current, unexpired ownership lease of the record's workflow execution. The
    /// check and the write are one atomic unit of work. A store may also fail for reasons of its own, such as a
    /// write failure it does not retry. It has then written nothing, unless the failure came from the commit
    /// itself, which may still have been applied.
    /// </summary>
    /// <remarks>
    /// The drainer treats only <see cref="RuntimeStaleFencingTokenException"/> as a refusal of the record. After any
    /// other failure of this call, except an <see cref="OperationCanceledException"/>, it writes the record through
    /// <see cref="IWorkflowSchedulerPoisonStore.RecordAsync"/>, without the fence. So a failure that could be a
    /// lost lease, such as a write conflict on the ownership state, must leave as
    /// <see cref="RuntimeStaleFencingTokenException"/> when the lease is no longer the caller's.
    /// </remarks>
    /// <exception cref="RuntimeStaleFencingTokenException">
    /// The execution has no ownership lease, the lease has expired, or another lease holds it. Nothing was written.
    /// </exception>
    /// <exception cref="NotSupportedException"><see cref="CanRecordFenced"/> is <see langword="false"/>.</exception>
    ValueTask<RuntimeSchedulerPoisonRecord> RecordFencedAsync(
        RuntimeSchedulerPoisonRecord record,
        RuntimeExecutionFence fence,
        CancellationToken cancellationToken = default);
}

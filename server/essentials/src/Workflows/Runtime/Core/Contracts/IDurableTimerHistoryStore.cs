using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Optional timer-owner atomic history responsibility. Legacy timer stores do not advertise this capability.</summary>
public interface IDurableTimerHistoryStore : IDurableTimerStore
{
    bool SupportsHistoryResponsibility => false;
    /// <summary>Qualifies bounded source inspection against the actual selected history reader.</summary>
    bool CanInspectHistorySources(IRuntimeHistoryStore history) => false;
    /// <summary>Qualifies discharge against the actual inner native checkpoint owner; interface presence alone is insufficient.</summary>
    bool CanDischargeUnrecordedCompletion(IRuntimeCheckpointCommitStore checkpointStore) => false;
    /// <summary>One bounded keyset page of active due timers, ordered by deadline with a stable provider-owned identity tie order.
    /// The opaque continuation is bound to the current scope, cutoff and page limit. List/delete owners
    /// must support this traversal so held captured sources cannot hide later timers behind a full page.</summary>
    ValueTask<RuntimeStorePage<DurableTimer>> ListDuePageAsync(DateTimeOffset asOf, RuntimeStorePageRequest page, CancellationToken cancellationToken = default);
    ValueTask<bool> HasPendingHistoryAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryAsync(DurableTimerHistoryQuery query, CancellationToken cancellationToken = default);
    ValueTask<bool> AcknowledgeHistoryAsync(string workflowExecutionId, string timerId, string historyEventId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryAsync(RuntimeDurableTimerClaim claim,
        RuntimeHistoryObservation observation, CancellationToken cancellationToken = default);
    ValueTask RetireWithHistoryAsync(string workflowExecutionId, string timerId, RuntimeHistoryObservation observation, CancellationToken cancellationToken = default);
    /// <summary>Discharges a bounded retired-source prefix only after the engine proves successful Ephemeral completion
    /// without a native record. True permits queue advancement; false leaves that advancement owed.</summary>
    ValueTask<bool> DischargeUnrecordedCompletionAsync(RuntimeCheckpointCommit completion, int limit = 256, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(new NotSupportedException("This timer owner cannot discharge unrecorded completion responsibility."));
}

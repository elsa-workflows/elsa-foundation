using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>
/// Allocates independent delivery observations through the native owner's checkpoint/deletion gate.
/// Source authorization precedes this port; persistence access and retained ownership are rechecked here.
/// This does not execute work, create a history head or advance runtime-checkpoint coverage.
/// </summary>
internal sealed class InMemoryRuntimeHistoryObservationWriter(InMemoryRuntimeCheckpointStoreState state,
    IPersistenceAccessContextAccessor access, IWorkflowExecutionStateStore executions, TimeProvider clock)
    : IRuntimeHistoryObservationWriter
{
    public bool CanRecordHistory(IRuntimeHistoryStore selectedStore)
    {
        if (executions is not InMemoryWorkflowExecutionStateStore native || !ReferenceEquals(native.CheckpointState, state) ||
            selectedStore is not InMemoryRuntimeHistoryStore reader || !reader.UsesAccessor(access)) return false;
        try { return reader.Uses(state, access.Current.RequireScope()); }
        catch (InvalidOperationException) { return false; }
    }

    public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        RuntimeHistorySafety.ValidateObservation(observation);
        cancellationToken.ThrowIfCancellationRequested();
        return RecordCoreAsync(observation, cancellationToken);
    }

    private async ValueTask<RuntimeHistoryObservationResult> RecordCoreAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken)
    {
        var e = observation.Event;
        try
        {
            if (executions is not InMemoryWorkflowExecutionStateStore native || !ReferenceEquals(native.CheckpointState, state))
                throw new NotSupportedException("The observation owner does not share its native checkpoint/deletion boundary.");
            var context = access.Current;
            var scope = context.RequireScope().Value;
            var fingerprint = "observation:" + RuntimeHistoryIdentity.Compute(e);
            return await state.ExecuteWriteGateAsync<RuntimeHistoryObservationResult>(async token =>
            {
                var execution = await executions.FindAsync(e.WorkflowExecutionId, token);
                if (access.Current != context) throw new InvalidOperationException("Persistence access changed during observation admission.");
                if (execution is null) return new RuntimeHistoryObservationResult(false, null);
                context.EnsureTenantScope(execution.TenantId);
                lock (state.SyncRoot)
                {
                    var key = (scope, e.WorkflowExecutionId);
                    if (state.HistoryDeletedRuns.Contains(key)) return new(false, null);
                    var hasOwner = state.HistoryRunOwners.TryGetValue(e.WorkflowExecutionId, out var owner);
                    if (hasOwner && owner.Scope != scope)
                        throw Invalid("the native run belongs to another history scope");
                    if (!state.HistoryHeads.TryGetValue(key, out var head))
                    {
                        if (hasOwner) throw Invalid("the captured native owner has no retained head");
                        return new(false, null);
                    }
                    InMemoryRuntimeHistoryStore.ValidateHead(e.WorkflowExecutionId, head);
                    if (!hasOwner || owner.Policy is null || !owner.Policy.IsEquivalentTo(head.Policy) ||
                        !head.Policy.IsEquivalentTo(observation.Policy) || !head.Policy.IsEquivalentTo(execution.HistoryCapturePolicy) ||
                        execution.HistoryCaptureEnabledAtAdmission == false)
                        throw Invalid("observation policy disagrees with its retained native owner");
                    var receipt = (scope, e.WorkflowExecutionId, e.HistoryEventId);
                    if (state.HistoryCandidateFingerprints.TryGetValue(receipt, out var existing))
                    {
                        if (existing != fingerprint) throw Invalid("an immutable observation has conflicting content");
                        if (!state.HistoryReceiptPositions.TryGetValue(receipt, out var position) || position <= 0 || position > head.LastSequence)
                            throw Invalid("the observation receipt has an invalid retained position");
                        var retained = state.HistoryEventsById.GetValueOrDefault(receipt);
                        if (position <= head.PurgedThroughSequence)
                        {
                            if (retained is not null) throw Invalid("purged observation content remains in its exact projection");
                            return new(true, null);
                        }
                        if (retained is null || retained.WorkflowExecutionId != e.WorkflowExecutionId || retained.HistoryEventId != e.HistoryEventId ||
                            retained.Sequence != position || retained.Sequence < head.FirstSequence || retained.RecordedAt is null ||
                            retained.PhysicalCommitId != "owner-observation:" + e.HistoryEventId ||
                            fingerprint != "observation:" + RuntimeHistoryIdentity.Compute(retained))
                            throw Invalid("the observation receipt has unacknowledged missing content");
                        return new(true, retained);
                    }
                    if (state.HistoryReceiptPositions.ContainsKey(receipt) || state.HistoryEventsById.ContainsKey(receipt))
                        throw Invalid("an observation projection has no semantic receipt");
                    var stored = RuntimeHistoryIdentity.Position(e, checked(head.LastSequence + 1), "owner-observation:" + e.HistoryEventId, clock.GetUtcNow());
                    // Prepare the complete immutable head before publishing either projection.
                    var updated = head with { LastSequence = stored.Sequence,
                        FirstSequence = head.FirstSequence == 0 || head.FirstSequence > head.LastSequence ? stored.Sequence : head.FirstSequence };
                    state.PublishHistoryEvent(scope, stored, fingerprint);
                    state.HistoryHeads[key] = updated;
                    return new(true, stored);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (RuntimeHistoryReadFailures.IsMalformed(exception) && exception is not RuntimeHistoryIntegrityException)
        {
            throw new RuntimeHistoryIntegrityException(e.WorkflowExecutionId, "the native observation owner could not interpret its retained data", exception);
        }
        catch (Exception exception) when (exception is not RuntimeHistoryIntegrityException && RuntimeHistoryReadFailures.IsUnavailable(exception))
        {
            throw new RuntimeHistoryUnavailableException(e.WorkflowExecutionId, "record-observation", exception);
        }

        RuntimeHistoryIntegrityException Invalid(string detail) => new(e.WorkflowExecutionId, detail);
    }
}

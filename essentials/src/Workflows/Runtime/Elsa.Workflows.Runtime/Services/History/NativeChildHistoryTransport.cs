using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Revalidates diagnostic responsibility at the actual native actor handoff, independently of execution authority.</summary>
internal sealed class NativeChildHistoryTransport(IPostCommitOutboxLookupStore sources, IWorkflowDispatchStore dispatches,
    IWorkflowExecutionStateStore executions, IRuntimeHistoryStore history, IRuntimeHistoryObservationWriter observations,
    IPersistenceAccessContextAccessor access, TimeProvider clock)
{
    internal bool CanRecord(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader,
        IWorkflowExecutionStateStore? selectedExecutions, TimeProvider selectedClock, IWorkflowDispatchStore? selectedDispatches = null) =>
        ReferenceEquals(writer, observations) && ReferenceEquals(reader, history) && writer.CanRecordHistory(reader) &&
        ReferenceEquals(executions, selectedExecutions) && ReferenceEquals(clock, selectedClock) &&
        (selectedDispatches is null || ReferenceEquals(dispatches, selectedDispatches)) && sources.CanInspectHistorySources(reader);

    internal async ValueTask ValidateAsync(PostCommitDeliveryContext source, string targetWorkflow, string? bookmarkId, CancellationToken token)
    {
        try
        {
            var context = access.Current;
            if (context.RequireScope() != source.Scope || !CanRecord(observations, history, executions, clock))
                throw new NotSupportedException("The selected native child source/history boundary is unavailable.");
            var current = await sources.FindDurableAsync(source.SourceOutboxItemId, token)
                ?? throw new InvalidDataException("The native child source is missing.");
            Check(current);
            var cause = current.Intent.HistoryCausation ?? throw new InvalidDataException("The native child source has no original history cause.");
            var identity = new WorkflowDispatchIdentity(current.Intent.WorkflowExecutionId,
                current.Intent.ActivityExecutionId ?? throw new InvalidDataException("The native child source has no calling occurrence."));
            var start = current.Intent.Kind == WorkflowDispatchLifecycle.StartChildIntentKind && current.Intent.IntentId == identity.StartIntentId &&
                targetWorkflow == identity.ChildWorkflowExecutionId && bookmarkId is null;
            var resume = current.Intent.Kind == WorkflowDispatchLifecycle.ResumeParentIntentKind && current.Intent.IntentId == identity.ParentResumeIntentId &&
                targetWorkflow == current.Intent.WorkflowExecutionId && bookmarkId == identity.WaitBookmarkId;
            if (!start && !resume) throw new InvalidDataException("The native child source addresses a different recipient.");
            var edge = await dispatches.FindAsync(identity.DispatchId, token)
                ?? throw new InvalidDataException("The admitted native child edge is missing.");
            if (edge.ParentWorkflowExecutionId != current.Intent.WorkflowExecutionId || edge.ParentActivityExecutionId != current.Intent.ActivityExecutionId ||
                edge.ChildWorkflowExecutionId != identity.ChildWorkflowExecutionId ||
                resume && (edge.Mode != WorkflowDispatchMode.WaitForCompletion || !WorkflowDispatchLifecycle.IsTerminal(edge.Status)) ||
                cause.WorkflowExecutionId != (start ? edge.ParentWorkflowExecutionId : edge.ChildWorkflowExecutionId) || cause.DeliveryId != source.SourceOutboxItemId ||
                string.IsNullOrWhiteSpace(cause.HistoryEventId)) throw new InvalidDataException("The native child history source disagrees with its admitted edge.");
            var origin = await executions.FindAsync(cause.WorkflowExecutionId!, token)
                ?? throw new InvalidDataException("The original native child history owner is missing.");
            NativeIngressCheckpointAuthority.EnsureHistoryScope(context, origin);
            if (origin.HistoryCapturePolicy is not { PolicyId: "runtime-history-v5", Version: 5 } || resume && !origin.Status.IsTerminal())
                throw new InvalidDataException("The native child origin has no qualified retained history decision.");
            var recorded = await history.FindEventAsync(origin.WorkflowExecutionId, cause.HistoryEventId, token);
            if (recorded is null || recorded.Authority != RuntimeHistoryAuthority.CommittedTransition ||
                recorded.Name != (start ? "ChildWorkflowDispatchRequested" : "ChildWorkflowSettled") ||
                recorded.Metadata.GetValueOrDefault("history.sourceDeliveryId") != current.OutboxItemId ||
                recorded.Metadata.GetValueOrDefault("history.relatedWorkflowExecutionId") != (start ? edge.ChildWorkflowExecutionId : edge.ParentWorkflowExecutionId))
                throw new InvalidDataException("The native child source has no matching committed origin.");
            Check(await sources.FindDurableAsync(source.SourceOutboxItemId, token)
                ?? throw new InvalidDataException("The native child source disappeared before handoff."));
            if (access.Current != context) throw new InvalidDataException("Native child handoff access changed.");

            void Check(RuntimePostCommitOutboxItem item)
            {
                if (item.SourceKind != RuntimeDeliverySourceKind.WorkflowCheckpoint || item.OutboxItemId != source.SourceOutboxItemId ||
                    RuntimeCheckpointCommitFingerprint.ComputeIntent(item.Intent) != source.IntentFingerprint || item.StimulusDeliverySourceScope != source.Scope)
                    throw new InvalidDataException("The committed native child source changed.");
                if (item.Status != RuntimePostCommitOutboxStatus.Delivering || item.DeliveringOwnerId != source.Claim.OwnerId ||
                    item.DeliveryFencingToken != source.Claim.FencingToken || item.DeliveryStartedAt != source.Claim.ClaimedAt ||
                    item.DeliveryVisibleAfter is not { } deadline || deadline <= clock.GetUtcNow())
                    throw new RuntimePostCommitOutboxStaleClaimException(item.OutboxItemId, source.Claim.OwnerId, source.Claim.FencingToken,
                        item.DeliveringOwnerId, item.DeliveryFencingToken, item.Status);
            }
        }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception failure)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, failure, RuntimeHistoryReadFailures.IsUnavailable(failure)); }
    }
}

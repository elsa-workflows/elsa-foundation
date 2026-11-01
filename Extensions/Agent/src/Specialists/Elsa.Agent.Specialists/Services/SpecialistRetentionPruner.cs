using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

internal sealed record SpecialistRetentionPolicy(TimeSpan ContentRetention);
internal sealed record SpecialistRetentionSweepResult(int Purged, IReadOnlyDictionary<string, int> Pinned, string? NextHandle);

// Eligibility comes from the existing native workflow and physical settlement source. No secret key
// is needed to remove expired content, and no native/business-output retention policy is changed.
internal sealed class SpecialistRetentionPruner(ISpecialistTurnStore turns, IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities, IWorkflowExecutableStore executables, IPostCommitOutboxLookupStore sources,
    IPersistenceAccessContextAccessor access, TimeProvider time, SpecialistRetentionPolicy policy)
{
    internal async ValueTask<SpecialistRetentionSweepResult> PrunePageAsync(SpecialistRetentionQuery query, CancellationToken cancellationToken)
    {
        var scope = access.Current.RequireScope();
        access.Current.EnsureTenantScope(query.TenantId);
        if (query.PersistencePartition != scope.Value) throw SpecialistModelRunner.Failure("specialist.retention.scope", "The retention scan requires its selected ordinary partition.");
        var page = await turns.QueryRetentionAsync(query, cancellationToken);
        var pinned = new Dictionary<string, int>(StringComparer.Ordinal);
        var purged = 0;
        foreach (var candidate in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var now = time.GetUtcNow();
                var reason = await PinReasonAsync(candidate, now, cancellationToken);
                if (reason is null && await turns.PurgeContentAsync(candidate, now, cancellationToken)) { purged++; continue; }
                reason ??= "turn-changed";
                pinned[reason] = pinned.GetValueOrDefault(reason) + 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var failure = SpecialistModelRunner.Failure("specialist.retention.failed", "The specialist retention operation could not be confirmed.", exception);
                failure.Data["contextId"] = candidate.Handle;
                failure.Data["workflowExecutionId"] = candidate.Identity.WorkflowExecutionId;
                throw failure;
            }
        }
        return new(purged, pinned, page.NextHandle);
    }

    private async ValueTask<string?> PinReasonAsync(SpecialistRetentionCandidate candidate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!SpecialistRetentionRules.Settled(candidate.Status) || candidate.Deadline >= now) return "turn-unsettled";
        var identity = candidate.Identity;
        if (candidate.Conversation is { } admission)
        {
            var conversation = await turns.ReadConversationAsync(admission.ConversationId, identity, cancellationToken);
            if (conversation is null) return "conversation-unavailable";
            if (conversation.ActiveTurnHandle is not null || conversation.OutcomeUnknown) return "conversation-unsettled";
        }
        var workflow = await workflows.FindAsync(identity.WorkflowExecutionId, cancellationToken);
        if (workflow is null) return "owner-unavailable";
        if (!workflow.Status.IsTerminal()) return "owner-active";
        if (workflow.CompletedAt is not { } completed || completed > now - policy.ContentRetention) return "retention-window";
        var scope = access.Current.RequireScope();
        access.Current.EnsureTenantScope(workflow.TenantId);
        if (workflow.TenantId != identity.TenantId || workflow.Partition?.Value != scope.Value || workflow.Partition.Value != identity.PersistencePartition ||
            workflow.PinnedExecutable.ArtifactId != identity.ArtifactId || workflow.PinnedExecutable.ArtifactHash != identity.ArtifactHash) return "owner-mismatch";
        var executable = await executables.FindAsync(identity.ArtifactId, cancellationToken);
        if (executable?.Identity.ArtifactHash != identity.ArtifactHash) return "artifact-unavailable";
        var deliveries = candidate.DeliveryProtocol == 0 ? [new SpecialistTurnDelivery(identity)] : candidate.Deliveries;
        if (candidate.DeliveryProtocol is not (0 or 1) || deliveries is null || deliveries.Count is < 1 or > 64 || candidate.Applications is { Count: > 64 }) return "delivery-unavailable";
        foreach (var delivery in deliveries)
        {
            if (!SpecialistTurnDeliveryRules.SameLogical(identity, delivery.Identity)) return "delivery-mismatch";
            var parent = await activities.FindAsync(identity.WorkflowExecutionId, delivery.Identity.ActivityExecutionId, cancellationToken);
            if (parent is null || !executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node)) return "delivery-unavailable";
            if (SpecialistNativeBinding.CancellationIdentity(workflow, parent, node) != delivery.Identity) return "delivery-mismatch";
            if (parent.Status == ActivityExecutionStatus.Completed)
            {
                var application = candidate.Applications?.SingleOrDefault(item => item.Identity == delivery.Identity);
                if (application is null || parent.CompletedAt != application.AppliedAt || delivery.ChildInvocationId is { } child && child != application.ChildInvocationId)
                    return "application-unconfirmed";
                var payload = new SpecialistAppliedPayload(1, candidate.Handle, delivery.Identity, application.ChildInvocationId, application.AppliedAt);
                var source = await sources.FindDurableAsync(application.SourceId, cancellationToken);
                if (source is null || source.Status != RuntimePostCommitOutboxStatus.Delivered || source.OutboxItemId != application.SourceId ||
                    RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent) != application.SourceFingerprint ||
                    !source.Intent.IsEquivalentTo(SpecialistSettlementBoundary.Intent(payload))) return "application-pending";
            }
            else if (parent.Status is not (ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded))
                return "delivery-unsettled";
        }
        // Terminal native owners cannot schedule more work; still refuse a changed/deleted owner
        // observed during the metadata/source reads before the conditional journal mutation.
        var current = await workflows.FindAsync(identity.WorkflowExecutionId, cancellationToken);
        if (current is null || current.Status != workflow.Status || current.CompletedAt != workflow.CompletedAt || current.UpdatedAt != workflow.UpdatedAt ||
            current.PinnedExecutable != workflow.PinnedExecutable || current.Partition != workflow.Partition || current.TenantId != workflow.TenantId) return "owner-changed";
        return null;
    }
}

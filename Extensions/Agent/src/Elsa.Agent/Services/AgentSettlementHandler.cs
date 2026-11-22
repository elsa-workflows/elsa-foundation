using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Journal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Services;

internal sealed class AgentSettlementHandler(AgentSettlementBoundary boundary, IActivityExecutionStateStore activities,
    IPostCommitOutboxLookupStore sources, IPersistenceAccessContextAccessor access, TimeProvider time,
    AgentTurnJournal journal, IAgentTurnStore turns) : IRuntimePostCommitDurableSourceIntentHandler
{
    public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(null, intent.Kind,
            new NotSupportedException("Agent settlement requires its physically committed native source."), false));
    }

    /// <exception cref="AgentTurnException">A settlement failure retaining its cause and exact source/workflow identity.</exception>
    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        try { await HandleCoreAsync(source, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (RuntimePostCommitDeliveryException exception)
        {
            exception.Data["contextId"] = source.SourceOutboxItemId;
            exception.Data["workflowExecutionId"] = source.Intent.WorkflowExecutionId;
            throw;
        }
        catch (Exception exception) { throw AgentSettlementBoundary.Failure(source.SourceOutboxItemId, source.Intent.WorkflowExecutionId, exception); }
    }

    private async ValueTask HandleCoreAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        await VerifySourceAsync(source, cancellationToken);
        if (source.Intent.Kind == AgentSettlementBoundary.CancelKind)
        {
            await CancelCoreAsync(source, cancellationToken);
            return;
        }
        AgentAppliedPayload payload;
        try
        {
            payload = source.Intent.Payload?.Deserialize<AgentAppliedPayload>(AgentSettlementBoundary.Json) ?? throw AgentSettlementBoundary.Invalid();
            if (payload.Version != 1 || !AgentSettlementBoundary.Intent(payload).IsEquivalentTo(source.Intent)) throw AgentSettlementBoundary.Invalid();
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, false); }
        var (workflow, executable) = await boundary.WorkflowAsync(payload.Identity.WorkflowExecutionId, cancellationToken);
        var parent = await activities.FindAsync(workflow.WorkflowExecutionId, payload.Identity.ActivityExecutionId, cancellationToken) ?? throw AgentSettlementBoundary.Invalid();
        if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node) ||
            await boundary.AppliedAsync(workflow, parent, node, cancellationToken) != payload) throw AgentSettlementBoundary.Invalid();
        _ = await journal.ResultAsync(payload.Handle, payload.Identity, payload.ChildInvocationId, cancellationToken,
            request => AgentNativeBinding.VerifyRequest(request, payload.Identity, parent, node));
        // Reads may outlive a delivery claim. Reopen the physical source immediately before the idempotent metadata write.
        await VerifySourceAsync(source, cancellationToken);
        var application = new AgentTurnApplication(payload.Identity, payload.ChildInvocationId, source.SourceOutboxItemId, source.IntentFingerprint, payload.AppliedAt);
        if (await turns.ApplyAsync(payload.Handle, application, cancellationToken) is null)
            throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Permanent, "agent.settlement.conflict", "The native agent delivery cannot accept this application source.", AgentSettlementBoundary.Invalid());
    }

    private async ValueTask CancelCoreAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        var payload = source.Intent.Payload?.Deserialize<AgentCancelledPayload>(AgentSettlementBoundary.Json) ?? throw AgentSettlementBoundary.Invalid();
        if (payload.Version != 1 || !AgentSettlementBoundary.Intent(payload).IsEquivalentTo(source.Intent)) throw AgentSettlementBoundary.Invalid();
        var (workflow, executable) = await boundary.WorkflowAsync(payload.Identity.WorkflowExecutionId, cancellationToken);
        var parent = await activities.FindAsync(workflow.WorkflowExecutionId, payload.Identity.ActivityExecutionId, cancellationToken) ?? throw AgentSettlementBoundary.Invalid();
        if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node) ||
            await boundary.CancelledAsync(workflow, parent, node, cancellationToken) != payload) throw AgentSettlementBoundary.Invalid();
        await VerifySourceAsync(source, cancellationToken);
        _ = await turns.CancelAsync(new(payload.Identity, payload.ChildInvocationId, source.SourceOutboxItemId, source.IntentFingerprint, payload.CancelledAt), cancellationToken);
    }

    private async ValueTask VerifySourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        try { await VerifySourceCoreAsync(source, cancellationToken); }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception,
                exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException));
        }
    }

    private async ValueTask VerifySourceCoreAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Intent.Kind is not (AgentSettlementBoundary.AppliedKind or AgentSettlementBoundary.CancelKind) || access.Current.AccessPolicy != PersistenceAccessPolicy.Ordinary || source.Scope != access.Current.RequireScope())
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, AgentSettlementBoundary.Invalid(), false);
        var current = await sources.FindDurableAsync(source.SourceOutboxItemId, cancellationToken) ?? throw AgentSettlementBoundary.Invalid();
        if (current.OutboxItemId != source.SourceOutboxItemId || RuntimeCheckpointCommitFingerprint.ComputeIntent(current.Intent) != source.IntentFingerprint)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, AgentSettlementBoundary.Invalid(), false);
        if (current.Status != RuntimePostCommitOutboxStatus.Delivering || current.DeliveringOwnerId != source.Claim.OwnerId ||
            current.DeliveryFencingToken != source.Claim.FencingToken || current.DeliveryStartedAt != source.Claim.ClaimedAt ||
            current.DeliveryVisibleAfter != source.Claim.VisibleAfter || current.DeliveryVisibleAfter <= time.GetUtcNow())
            throw new RuntimePostCommitOutboxStaleClaimException(current.OutboxItemId, source.Claim.OwnerId, source.Claim.FencingToken,
                current.DeliveringOwnerId, current.DeliveryFencingToken, current.Status);
    }
}

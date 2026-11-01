using System.Text.Json;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

internal sealed class SpecialistSettlementHandler(SpecialistSettlementBoundary boundary, IActivityExecutionStateStore activities,
    IPostCommitOutboxLookupStore sources, IPersistenceAccessContextAccessor access, TimeProvider time,
    SpecialistTurnJournal journal, ISpecialistTurnStore turns) : IRuntimePostCommitDurableSourceIntentHandler
{
    public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(null, intent.Kind,
            new NotSupportedException("Specialist settlement requires its physically committed native source."), false));
    }

    /// <exception cref="SpecialistTurnException">A settlement failure retaining its cause and exact source/workflow identity.</exception>
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
        catch (Exception exception) { throw SpecialistSettlementBoundary.Failure(source.SourceOutboxItemId, source.Intent.WorkflowExecutionId, exception); }
    }

    private async ValueTask HandleCoreAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        await VerifySourceAsync(source, cancellationToken);
        SpecialistAppliedPayload payload;
        try
        {
            payload = source.Intent.Payload?.Deserialize<SpecialistAppliedPayload>(SpecialistSettlementBoundary.Json) ?? throw SpecialistSettlementBoundary.Invalid();
            if (payload.Version != 1 || !SpecialistSettlementBoundary.Intent(payload).IsEquivalentTo(source.Intent)) throw SpecialistSettlementBoundary.Invalid();
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, false); }
        var (workflow, executable) = await boundary.WorkflowAsync(payload.Identity.WorkflowExecutionId, cancellationToken);
        var parent = await activities.FindAsync(workflow.WorkflowExecutionId, payload.Identity.ActivityExecutionId, cancellationToken) ?? throw SpecialistSettlementBoundary.Invalid();
        if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node) ||
            await boundary.AppliedAsync(workflow, parent, node, cancellationToken) != payload) throw SpecialistSettlementBoundary.Invalid();
        _ = await journal.ResultAsync(payload.Handle, payload.Identity, payload.ChildInvocationId, cancellationToken,
            request => SpecialistNativeBinding.VerifyRequest(request, payload.Identity, parent, node));
        // Reads may outlive a delivery claim. Reopen the physical source immediately before the idempotent metadata write.
        await VerifySourceAsync(source, cancellationToken);
        var application = new SpecialistTurnApplication(payload.Identity, payload.ChildInvocationId, source.SourceOutboxItemId, source.IntentFingerprint, payload.AppliedAt);
        if (await turns.ApplyAsync(payload.Handle, application, cancellationToken) is null)
            throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Permanent, "specialist.settlement.conflict", "The native specialist delivery cannot accept this application source.", SpecialistSettlementBoundary.Invalid());
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
        if (source.Intent.Kind != SpecialistSettlementBoundary.AppliedKind || access.Current.AccessPolicy != PersistenceAccessPolicy.Ordinary || source.Scope != access.Current.RequireScope())
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, SpecialistSettlementBoundary.Invalid(), false);
        var current = await sources.FindDurableAsync(source.SourceOutboxItemId, cancellationToken) ?? throw SpecialistSettlementBoundary.Invalid();
        if (current.OutboxItemId != source.SourceOutboxItemId || RuntimeCheckpointCommitFingerprint.ComputeIntent(current.Intent) != source.IntentFingerprint)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, SpecialistSettlementBoundary.Invalid(), false);
        if (current.Status != RuntimePostCommitOutboxStatus.Delivering || current.DeliveringOwnerId != source.Claim.OwnerId ||
            current.DeliveryFencingToken != source.Claim.FencingToken || current.DeliveryStartedAt != source.Claim.ClaimedAt ||
            current.DeliveryVisibleAfter != source.Claim.VisibleAfter || current.DeliveryVisibleAfter <= time.GetUtcNow())
            throw new RuntimePostCommitOutboxStaleClaimException(current.OutboxItemId, source.Claim.OwnerId, source.Claim.FencingToken,
                current.DeliveringOwnerId, current.DeliveryFencingToken, current.Status);
    }
}

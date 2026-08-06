using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Recovers the durable Prepared frontier before a coalesced drain dispatches scheduler work. Source-bound
/// reservations retain their original durable source as the only progression authority; only a leading,
/// gap-free source-free prefix can be deterministically replayed and terminally folded.
/// </summary>
public sealed class CoalescingRuntimeCheckpointRecoveryCoordinator(
    IRuntimeCheckpointPreparedLedgerStore preparedLedgerStore,
    IRuntimeSchedulerWorkItemResolver schedulerWorkItemResolver,
    IRuntimeCheckpointPreparationReplayer preparationReplayer,
    IRuntimeExecutionOwnershipContextAccessor ownershipContextAccessor) : IRuntimeCheckpointPreparedRecoveryCoordinator
{
    /// <summary>
    /// Routes and adopts the complete durable Prepared frontier for one workflow. Unsafe, incomplete, or ambiguous
    /// source shapes fail closed without replaying, folding, or dispatching a synthetic source.
    /// </summary>
    public async ValueTask<RuntimeCheckpointPreparedRecoveryResult> RecoverPreparedAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var activeLease = ownershipContextAccessor.Current;
        if (activeLease is null)
            throw Fail(workflowExecutionId, "no active execution-ownership lease is available");
        if (!StringComparer.Ordinal.Equals(activeLease.WorkflowExecutionId, workflowExecutionId))
            throw Fail(workflowExecutionId, $"the active execution-ownership lease belongs to workflow '{activeLease.WorkflowExecutionId}'");
        var recoveryFence = activeLease.ToFence();

        var reservations = await PageAllAsync(workflowExecutionId, cancellationToken);
        if (reservations.Count == 0)
            return RuntimeCheckpointPreparedRecoveryResult.Ready;

        if (!HasContiguousOrders(reservations))
            throw Fail(workflowExecutionId, "the Prepared frontier is not contiguous");

        var router = new RuntimeCheckpointRecoveryRouter(schedulerWorkItemResolver);
        var routes = new RuntimeCheckpointRecoveryResolution[reservations.Count];
        for (var index = 0; index < reservations.Count; index++)
        {
            var resolution = await router.ResolveAsync(reservations[index].RecoveryAuthority, cancellationToken);
            routes[index] = resolution;
            if (resolution.Status is not (RuntimeCheckpointRecoveryStatus.Absent or RuntimeCheckpointRecoveryStatus.Exact))
                throw Fail(workflowExecutionId, $"source '{reservations[index].CommitId}' resolved as {resolution.Status}");
            if (resolution.Status == RuntimeCheckpointRecoveryStatus.Exact && resolution.WorkItem is null)
                throw Fail(workflowExecutionId, $"source '{reservations[index].CommitId}' resolved without an exact work item");
        }

        var firstSourceBound = Array.FindIndex(routes, route => route.Route == RuntimeCheckpointRecoveryRoute.SourceBound);
        if (firstSourceBound >= 0)
        {
            if (routes.Skip(firstSourceBound).Any(route => route.Route != RuntimeCheckpointRecoveryRoute.SourceBound))
                throw Fail(workflowExecutionId, "a source-free reservation appears after a source-bound reservation");
        }

        var sourceFreeCount = firstSourceBound < 0 ? reservations.Count : firstSourceBound;
        if (sourceFreeCount > 0)
        {
            var sourceFree = reservations.Take(sourceFreeCount).ToArray();
            // Rehydration is read-only and completes before the provider receives any mutation request. The provider
            // then validates/adopts the current binding and terminally folds the exact set in one atomic transaction.
            var preparedCommits = new RuntimeCheckpointPreparedCommit[sourceFree.Length];
            for (var index = 0; index < sourceFree.Length; index++)
                preparedCommits[index] = await preparationReplayer.RehydrateAsync(sourceFree[index], cancellationToken);

            var members = preparedCommits
                .Select((prepared, index) => new RuntimeCheckpointPreparedFoldMember(
                    prepared.Token,
                    prepared.Decision.Mode == RuntimeCheckpointPersistenceMode.Skip
                        ? RuntimeCheckpointPreparedDisposition.Skipped
                        : RuntimeCheckpointPreparedDisposition.Committed,
                    sourceFree[index].CurrentAuthorityFence,
                    sourceFree[index].AuthorityRevision,
                    prepared))
                .ToArray();
            var foldRequest = new RuntimeCheckpointPreparedFoldRequest(
                workflowExecutionId,
                members,
                sourceFree[^1].Provenance.WorkflowCheckpointOrder,
                new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
                recoveryFence,
                RuntimeCheckpointRecoveryRoute.SourceFree);
            foldRequest = foldRequest with
            {
                FoldedStateChanges = RuntimeCheckpointPreparedFoldBuilder.Build(foldRequest)
            };
            var foldResult = await preparedLedgerStore.CommitPreparedFoldAsync(foldRequest, cancellationToken);
            if (foldResult.Status is not (RuntimeCheckpointCommitStoreStatus.Committed or RuntimeCheckpointCommitStoreStatus.Replay))
                throw Fail(workflowExecutionId, $"atomic source-free adoption/fold returned {foldResult.Status}");
        }

        if (firstSourceBound < 0)
            return RuntimeCheckpointPreparedRecoveryResult.Ready;

        // Folding a source-free prefix changes durable membership. Do not perform a second authority mutation or
        // dispatch from this stale snapshot. A subsequent pass observes the exact source-bound frontier and adopts it.
        if (sourceFreeCount > 0)
            return RuntimeCheckpointPreparedRecoveryResult.RetryRequired;

        // Source-free folding removes that prefix from the provider's Prepared membership. The remaining exact
        // source-bound suffix can therefore be adopted as its own complete set through the original inclusive max.
        var sourceBound = reservations.Skip(firstSourceBound).ToArray();
        var sourceBoundAdoption = await preparedLedgerStore.AdoptPreparedAsync(
            CreateAdoptionRequest(workflowExecutionId, RuntimeCheckpointRecoveryRoute.SourceBound, recoveryFence, sourceBound),
            cancellationToken);
        if (sourceBoundAdoption.Status is not (RuntimeCheckpointPreparedAdoptionStatus.Adopted or RuntimeCheckpointPreparedAdoptionStatus.Replay))
            throw Fail(workflowExecutionId, $"source-bound adoption returned {sourceBoundAdoption.Status}");
        return RuntimeCheckpointPreparedRecoveryResult.Ready;
    }

    private async ValueTask<IReadOnlyList<RuntimeCheckpointPreparedReservation>> PageAllAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var reservations = new List<RuntimeCheckpointPreparedReservation>();
        string? cursor = null;
        do
        {
            var page = await preparedLedgerStore.PagePreparedAsync(
                new RuntimeCheckpointPreparedQuery(
                    workflowExecutionId,
                    RuntimeCheckpointPreparedQuery.MaximumPageSize,
                    cursor),
                cancellationToken);
            reservations.AddRange(page.Reservations);
            cursor = page.NextCursor;
        } while (cursor is not null);

        return reservations;
    }

    private static bool HasContiguousOrders(IReadOnlyList<RuntimeCheckpointPreparedReservation> reservations)
    {
        for (var index = 1; index < reservations.Count; index++)
        {
            if (reservations[index].Provenance.WorkflowCheckpointOrder !=
                reservations[index - 1].Provenance.WorkflowCheckpointOrder + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static RuntimeCheckpointPreparedAdoptionRequest CreateAdoptionRequest(
        string workflowExecutionId,
        RuntimeCheckpointRecoveryRoute route,
        RuntimeExecutionFence targetFence,
        IReadOnlyList<RuntimeCheckpointPreparedReservation> reservations) =>
        new(
            workflowExecutionId,
            route,
            reservations[^1].Provenance.WorkflowCheckpointOrder,
            targetFence,
            reservations.Select(ToAdoptionMember).ToArray());

    private static RuntimeCheckpointPreparedAdoptionMember ToAdoptionMember(
        RuntimeCheckpointPreparedReservation reservation) =>
        new(
            reservation.CommitId,
            reservation.Token.LedgerToken,
            reservation.Provenance.WorkflowCheckpointOrder,
            reservation.InputFingerprint,
            reservation.Token.CanonicalInputReference,
            reservation.ExpectedFence,
            reservation.ExpectedOrderRevision,
            reservation.ExpectedContextRevision,
            reservation.RecoveryAuthority,
            reservation.CurrentAuthorityFence,
            reservation.AuthorityRevision);

    private static RuntimeCheckpointPreparedRecoveryException Fail(string workflowExecutionId, string reason) =>
        new(workflowExecutionId, reason);
}

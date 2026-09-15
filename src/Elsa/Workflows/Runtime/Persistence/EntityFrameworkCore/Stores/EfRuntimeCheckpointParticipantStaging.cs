using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Internal staging seam for the bounded R19 checkpoint slice.
/// </summary>
/// <remarks>
/// The direct R14-R18 stores own their public calls and therefore clear the change tracker around independent
/// writes. A checkpoint is different: all participants must remain tracked in one transaction. This seam reuses
/// their exact entity projections while deliberately exposing no domain or EF types outside the persistence
/// assembly.
/// </remarks>
internal static class EfRuntimeCheckpointParticipantStaging
{
    public static async ValueTask StageWorkflowExecutionAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<WorkflowExecutionState> change,
        string scope,
        CancellationToken cancellationToken)
    {
        var id = EfWorkflowExecutionStateStore.CreateId(scope, change.StateId);
        var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            candidate.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(change.StateId) &&
            candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(change.StateId),
            cancellationToken);

        if (row is null)
        {
            context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(
                change.State,
                scope,
                id,
                1));
            return;
        }

        _ = EfWorkflowExecutionStateStore.ReadChecked(row, scope, change.StateId);
        EfWorkflowExecutionStateStore.CopyToEntity(row, change.State, scope, id, checked(row.Revision + 1));
    }

    public static async ValueTask StageSchedulerAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<SchedulerState> change,
        string scope,
        CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, change.StateId);
        var row = await context.SchedulerStates.SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            candidate.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(change.StateId) &&
            candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(change.StateId),
            cancellationToken);

        if (row is null)
        {
            context.SchedulerStates.Add(EfSchedulerStateStore.ToEntity(
                change.State,
                scope,
                id,
                1));
            return;
        }

        _ = EfSchedulerStateStore.Read(row, scope, change.StateId);
        EfSchedulerStateStore.Copy(row, change.State, scope, checked(row.Revision + 1));
    }

    public static async ValueTask StageExecutionFenceAsync(
        BookmarkStateDbContext context,
        string scope,
        string workflowExecutionId,
        RuntimeExecutionFence expected,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var operationalStateId = $"ownership:{workflowExecutionId}";
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, operationalStateId);
        var row = await context.ExecutionLivenessStates.SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            candidate.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId) &&
            candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId) &&
            candidate.OperationalStateIdHash == EfRuntimeOperationalStoreSupport.Hash(operationalStateId) &&
            candidate.OperationalStateId == EfRuntimeOperationalStoreSupport.Encode(operationalStateId),
            cancellationToken);

        if (row is null)
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                0,
                RuntimeFencingRejectionReason.NoActiveLease);

        var state = EfExecutionLivenessStateStore.Read(row, scope, workflowExecutionId, operationalStateId);
        var lease = state.ExecutionLease;
        var currentToken = lease?.FencingToken ?? ReadHighestIssuedToken(state);
        if (lease is null)
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                currentToken,
                RuntimeFencingRejectionReason.NoActiveLease);
        if (lease.IsExpired(timeProvider.GetUtcNow()))
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                currentToken,
                RuntimeFencingRejectionReason.ExpiredLease);
        if (!StringComparer.Ordinal.Equals(lease.LeaseId, expected.LeaseId) ||
            !StringComparer.Ordinal.Equals(lease.OwnerId, expected.OwnerId) ||
            lease.FencingToken != expected.FencingToken)
        {
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                currentToken,
                RuntimeFencingRejectionReason.StaleToken);
        }

        // Revision is the provider-neutral optimistic fence. Touching it in the same transaction makes a concurrent
        // lease transition fail the whole checkpoint instead of allowing state and marker rows to commit together.
        row.Revision = checked(row.Revision + 1);
    }

    private static long ReadHighestIssuedToken(ExecutionLivenessState state)
    {
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var token))
            return token;
        return state.ExecutionLease?.FencingToken ?? 0;
    }
}

using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages inspection evidence and its derived hierarchy relation in a caller-owned checkpoint transaction.</summary>
/// <remarks>
/// The checkpoint writer owns the transaction, <c>SaveChanges</c>, commit, rollback, and replay marker. This seam
/// only changes tracked EF state and deliberately does not clear the change tracker.
/// </remarks>
internal static class EfRuntimeCheckpointInspectionParticipantStaging
{
    public static async ValueTask StageAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<ActivityExecutionInspectionProjection> change,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkflowExecutionId);
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(scope));

        var projection = change.State;
        ArgumentNullException.ThrowIfNull(projection);
        ActivityExecutionEfSupport.ValidateIdentityLength(expectedWorkflowExecutionId, nameof(expectedWorkflowExecutionId));
        ActivityExecutionEfSupport.Validate(projection);
        ActivityExecutionEfSupport.ValidateIdentityLength(change.StateId, nameof(change.StateId));
        if (!StringComparer.Ordinal.Equals(change.StateId, projection.ActivityExecutionId))
            throw new InvalidOperationException("Activity execution inspection StateId must match its model identity.");
        if (!StringComparer.Ordinal.Equals(projection.WorkflowExecutionId, expectedWorkflowExecutionId))
            throw new InvalidOperationException("Activity execution inspection workflow execution ID must match the checkpoint workflow execution ID.");
        if (change.Operation is not (RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
            throw new InvalidOperationException("The EF checkpoint writer can only project activity execution inspection upsert or delete changes.");
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Activity execution inspection changes must be staged inside a caller-owned EF transaction.");

        var inspectionId = ActivityExecutionEfSupport.CreateId(
            "inspection",
            scope,
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId);
        var inspectionRow = await context.ActivityExecutionInspections.SingleOrDefaultAsync(
            row => row.Id == inspectionId,
            cancellationToken);
        if (inspectionRow is null)
        {
            if (change.Operation == RuntimeStateChangeOperation.Upsert)
            {
                context.ActivityExecutionInspections.Add(
                    EfActivityExecutionInspectionStore.ToEntity(projection, scope, inspectionId, 1));
            }
        }
        else
        {
            var current = EfActivityExecutionInspectionStore.ReadChecked(
                inspectionRow,
                scope,
                projection.WorkflowExecutionId,
                projection.ActivityExecutionId);
            if (change.Operation == RuntimeStateChangeOperation.Delete)
                context.ActivityExecutionInspections.Remove(inspectionRow);
            else
                EfActivityExecutionInspectionStore.CopyToEntity(
                    inspectionRow,
                    projection,
                    scope,
                    inspectionId,
                    checked(inspectionRow.Revision + 1),
                    current);
        }

        var effectiveExecutionScope = ActivityExecutionEfSupport.EffectiveExecutionScope(projection);
        var hierarchyId = ActivityExecutionEfSupport.CreateId(
            "hierarchy",
            scope,
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId);
        var hierarchyRow = await context.ActivityExecutionHierarchies.SingleOrDefaultAsync(
            row => row.Id == hierarchyId,
            cancellationToken);

        if (change.Operation == RuntimeStateChangeOperation.Delete || string.IsNullOrWhiteSpace(effectiveExecutionScope))
        {
            if (hierarchyRow is not null)
            {
                _ = EfActivityExecutionHierarchyStore.ReadChecked(
                    hierarchyRow,
                    scope,
                    projection.WorkflowExecutionId,
                    projection.ActivityExecutionId);
                context.ActivityExecutionHierarchies.Remove(hierarchyRow);
            }

            return;
        }

        // The hierarchy record is derived from an effective scope. Preserve that fallback when the
        // projection carries its scope only in scheduling provenance.
        var hierarchyProjection = string.IsNullOrWhiteSpace(projection.ExecutionScopeId)
            ? projection with { ExecutionScopeId = effectiveExecutionScope }
            : projection;
        var hierarchy = ActivityExecutionHierarchyProjector.FromInspection(hierarchyProjection);
        if (hierarchyRow is null)
        {
            context.ActivityExecutionHierarchies.Add(
                EfActivityExecutionHierarchyStore.ToEntity(hierarchy, scope, hierarchyId, 1));
        }
        else
        {
            var current = EfActivityExecutionHierarchyStore.ReadChecked(
                hierarchyRow,
                scope,
                hierarchy.WorkflowExecutionId,
                hierarchy.ActivityExecutionId);
            EfActivityExecutionHierarchyStore.CopyToEntity(
                hierarchyRow,
                hierarchy,
                scope,
                hierarchyId,
                checked(hierarchyRow.Revision + 1),
                current);
        }
    }
}

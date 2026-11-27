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
        RuntimeDbContext context,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> changes,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
            return;

        var staged = changes
            .Select(change => Validated(context, change, scope, expectedWorkflowExecutionId, cancellationToken))
            .ToArray();

        // Both sets are loaded by immutable physical identity. Filtering on projections would turn a corrupt row into
        // a false insert/miss instead of allowing the authoritative content and projection checks to fail closed, and
        // an id a batch does not return is the same "row is null" case the per-item reads produced.
        var inspectionRows = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            staged.Select(entry => entry.InspectionId).ToArray(),
            batch => context.ActivityExecutionInspections.Where(row => batch.Contains(row.Id)),
            row => row.Id,
            cancellationToken);
        var hierarchyRows = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            staged.Select(entry => entry.HierarchyId).ToArray(),
            batch => context.ActivityExecutionHierarchies.Where(row => batch.Contains(row.Id)),
            row => row.Id,
            cancellationToken);

        foreach (var (change, inspectionId, hierarchyId) in staged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stage(
                context,
                change.State,
                scope,
                inspectionId,
                inspectionRows.GetValueOrDefault(inspectionId),
                hierarchyId,
                hierarchyRows.GetValueOrDefault(hierarchyId));
        }
    }

    private static (RuntimeStateChange<ActivityExecutionInspectionProjection> Change, string InspectionId, string HierarchyId) Validated(
        RuntimeDbContext context,
        RuntimeStateChange<ActivityExecutionInspectionProjection> change,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
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
        if (change.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException("The EF checkpoint writer can only project activity execution inspection upserts.");
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Activity execution inspection changes must be staged inside a caller-owned EF transaction.");

        return (
            change,
            ActivityExecutionEfSupport.CreateId("inspection", scope, projection.WorkflowExecutionId, projection.ActivityExecutionId),
            ActivityExecutionEfSupport.CreateId("hierarchy", scope, projection.WorkflowExecutionId, projection.ActivityExecutionId));
    }

    private static void Stage(
        RuntimeDbContext context,
        ActivityExecutionInspectionProjection projection,
        string scope,
        string inspectionId,
        ActivityExecutionInspectionEntity? inspectionRow,
        string hierarchyId,
        ActivityExecutionHierarchyEntity? hierarchyRow)
    {
        if (inspectionRow is null)
        {
            context.ActivityExecutionInspections.Add(
                EfActivityExecutionInspectionStore.ToEntity(projection, scope, inspectionId, 1));
        }
        else
        {
            var current = EfActivityExecutionInspectionStore.ReadChecked(
                inspectionRow,
                scope,
                projection.WorkflowExecutionId,
                projection.ActivityExecutionId);
            EfActivityExecutionInspectionStore.CopyToEntity(
                inspectionRow,
                projection,
                scope,
                inspectionId,
                checked(inspectionRow.Revision + 1),
                current);
        }

        var effectiveExecutionScope = ActivityExecutionEfSupport.EffectiveExecutionScope(projection);
        if (string.IsNullOrWhiteSpace(effectiveExecutionScope))
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

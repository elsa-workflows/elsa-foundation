using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

public sealed class ActivityExecutionLayoutReader(
    IWorkflowExecutionStateStore workflowExecutions,
    IActivityExecutionHierarchyStore hierarchy,
    IWorkflowExecutableStore executables,
    IWorkflowExecutableSourceReferenceStore sourceReferences,
    IActivityExecutionInspectionAuthorizationContext authorization)
{
    public async ValueTask<ActivityExecutionLayoutView?> ReadAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowExecutions.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || !authorization.CanInspectStructure(workflow))
            return null;
        var boundary = await hierarchy.FindBoundaryAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        if (boundary is null)
            return null;
        var executable = await executables.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            return null;

        var sourceReference = await FindExecutedReferenceAsync(workflow, cancellationToken);
        var sourceReferenceId = sourceReference?.SourceReferenceId ?? string.Empty;
        var segment = sourceReference?.LayoutSidecar.BoundarySegments.FirstOrDefault(x => OriginsEqual(x.BoundaryOrigin, boundary.InvocationOrigin));
        if (segment is null)
            return ActivityExecutionLayoutView.From(new(
                workflowExecutionId,
                activityExecutionId,
                executable.Identity.ArtifactId,
                sourceReferenceId,
                ActivityExecutionLayoutSelection.Automatic,
                boundary.InvocationOrigin,
                boundary.TemplateHash,
                [],
                [],
                []));

        var nodes = segment.Records.Where(x => executable.NodesById.ContainsKey(x.ExecutableNodeId)).ToArray();
        var connections = RuntimeFlowchartLayoutConnectionProjector.Project(executable, segment);
        var nested = await ReadNestedBoundariesAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        return ActivityExecutionLayoutView.From(new(
            workflowExecutionId,
            activityExecutionId,
            executable.Identity.ArtifactId,
            sourceReferenceId,
            ActivityExecutionLayoutSelection.ExecutedReference,
            boundary.InvocationOrigin,
            boundary.TemplateHash,
            nodes,
            connections,
            nested));
    }

    private async ValueTask<WorkflowExecutableSourceReference?> FindExecutedReferenceAsync(
        WorkflowExecutionState workflow,
        CancellationToken cancellationToken)
    {
        if (!workflow.SystemMetadata.TryGetValue(RuntimeMetadataKeys.SourceReferenceId, out var exactId))
            return null;

        var exact = await sourceReferences.FindAsync(exactId, cancellationToken);
        return exact is not null && StringComparer.Ordinal.Equals(exact.ArtifactId, workflow.PinnedExecutable.ArtifactId)
            ? exact
            : null;
    }

    private async ValueTask<IReadOnlyList<ActivityExecutionNestedBoundaryLayout>> ReadNestedBoundariesAsync(
        string workflowExecutionId,
        string rootActivityExecutionId,
        CancellationToken cancellationToken)
    {
        var result = new List<ActivityExecutionNestedBoundaryLayout>();
        string? cursor = null;
        do
        {
            var page = await hierarchy.ReadPageAsync(new(
                workflowExecutionId,
                rootActivityExecutionId,
                cursor,
                ActivityExecutionHierarchyPager.MaximumLimit,
                new HashSet<ActivityExecutionHierarchyInclude>(),
                authorization.AuthorizationProfile,
                authorization.TenantScope), cancellationToken);
            if (page is null)
                break;
            result.AddRange(page.Items
                .Where(x => x.Boundary is not null)
                .Select(x => new ActivityExecutionNestedBoundaryLayout(x.ActivityExecutionId, x.ExecutableNodeId, x.Boundary!.TemplateHash, x.Boundary.LayoutAvailable)));
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }

    private static bool OriginsEqual(ActivityInvocationOrigin left, ActivityInvocationOrigin right) =>
        left.Segments.Count == right.Segments.Count &&
        left.Segments.Zip(right.Segments).All(x => x.First.Kind == x.Second.Kind && StringComparer.Ordinal.Equals(x.First.Id, x.Second.Id));
}

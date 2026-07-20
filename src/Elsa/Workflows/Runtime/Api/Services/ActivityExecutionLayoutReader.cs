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
        var layoutRecords = segment?.Records ?? [];
        var layoutByNodeId = layoutRecords
            .Where(x => executable.NodesById.ContainsKey(x.ExecutableNodeId))
            .ToDictionary(x => x.ExecutableNodeId, StringComparer.Ordinal);
        var structuralNodes = ReadBoundaryNodes(executable, boundary)
            .OrderBy(x => x.ExecutableNodeId, StringComparer.Ordinal)
            .ToArray();
        var nodes = structuralNodes
            .Select(node =>
            {
                layoutByNodeId.TryGetValue(node.ExecutableNodeId, out var layout);
                return new ExecutableActivityLayoutRecord(
                    layout?.TemplateNodeId ?? node.Metadata.GetValueOrDefault("activity.templateNodeId") ?? node.AuthoredActivityId,
                    node.AuthoredActivityId,
                    node.ExecutableNodeId,
                    layout?.X ?? 0,
                    layout?.Y ?? 0,
                    layout?.Width,
                    layout?.Height,
                    layout?.AdditionalProperties,
                    node.ActivityType,
                    node.ActivityTypeVersion,
                    layout is not null);
            })
            .ToArray();
        if (nodes.Length == 0)
            nodes = layoutRecords.Where(x => executable.NodesById.ContainsKey(x.ExecutableNodeId)).ToArray();
        var connections = RuntimeFlowchartLayoutConnectionProjector.Project(executable, nodes)
            .Concat(RuntimeBpmnLayoutConnectionProjector.Project(executable, nodes))
            .ToArray();
        var nested = await ReadNestedBoundariesAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        return ActivityExecutionLayoutView.From(new(
            workflowExecutionId,
            activityExecutionId,
            executable.Identity.ArtifactId,
            sourceReferenceId,
            segment is null ? ActivityExecutionLayoutSelection.Automatic : ActivityExecutionLayoutSelection.ExecutedReference,
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

    private static IReadOnlyList<ExecutableNode> ReadBoundaryNodes(
        WorkflowExecutable executable,
        ActivityExecutionBoundary boundary)
    {
        if (string.IsNullOrWhiteSpace(boundary.ExecutableNodeId) ||
            !executable.NodesById.TryGetValue(boundary.ExecutableNodeId, out var root) ||
            !NodeMatchesOrigin(root, boundary.InvocationOrigin))
            return executable.Nodes.Where(x => NodeMatchesOrigin(x, boundary.InvocationOrigin)).ToArray();

        var result = new List<ExecutableNode>();
        var pending = new Stack<(ExecutableNode Node, bool IsRoot)>();
        pending.Push((root, true));
        while (pending.TryPop(out var current))
        {
            result.Add(current.Node);
            if (!current.IsRoot && IsReusableBoundary(current.Node))
                continue;

            foreach (var child in current.Node.ChildSlots.SelectMany(x => x.Activities).Reverse())
                pending.Push((child, false));
        }
        return result;
    }

    private static bool NodeMatchesOrigin(ExecutableNode node, ActivityInvocationOrigin boundaryOrigin)
    {
        if (!node.Metadata.TryGetValue("activity.invocationOrigin", out var encoded))
            return false;
        try
        {
            var segments = System.Text.Json.JsonSerializer.Deserialize<ActivityInvocationOriginSegment[]>(encoded);
            if (segments is null)
                return false;

            return OriginsEqual(new(segments), boundaryOrigin);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool IsReusableBoundary(ExecutableNode node) =>
        HasValue(node.Metadata, "activity.definitionId") &&
        HasValue(node.Metadata, "activity.definitionVersionId") &&
        HasValue(node.Metadata, "activity.version") &&
        HasValue(node.Metadata, "activity.templateHash");

    private static bool HasValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
}

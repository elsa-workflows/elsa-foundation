using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record ActivityExecutionHierarchyRootView(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutionScopeId,
    string DefinitionVersionId,
    string TemplateHash)
{
    public static ActivityExecutionHierarchyRootView From(ActivityExecutionHierarchyRoot root) =>
        new(root.WorkflowExecutionId, root.ActivityExecutionId, root.ExecutionScopeId, root.DefinitionVersionId, root.TemplateHash);
}

public sealed record ActivityExecutionHierarchyPageView(
    ActivityExecutionHierarchyRootView Root,
    long CommittedThroughSequence,
    int EffectiveLimit,
    IReadOnlyList<ActivityExecutionHierarchyItemView> Items,
    string? NextCursor)
{
    public static ActivityExecutionHierarchyPageView From(ActivityExecutionHierarchyPage page) => new(
        ActivityExecutionHierarchyRootView.From(page.Root), page.CommittedThroughSequence, page.EffectiveLimit,
        page.Items.Select(ActivityExecutionHierarchyItemView.From).ToArray(), page.NextCursor);
}

public sealed record ActivityExecutionHierarchyItemView(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ParentActivityExecutionId,
    string? SchedulingActivityExecutionId,
    int RelativeDepth,
    string? BranchId,
    string? IterationId,
    IReadOnlyCollection<string> OutcomeNames,
    int BookmarkCount,
    int IncidentCount,
    int BlockingIncidentCount,
    ActivityExecutionAttemptView? Attempt,
    ActivityExecutionBoundaryView? Boundary,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionHierarchyItemView From(ActivityExecutionHierarchyItem item) => new(
        item.ActivityExecutionId, item.WorkflowExecutionId, item.ExecutableNodeId, item.AuthoredActivityId,
        item.ActivityType, item.ActivityTypeVersion, item.Status.ToString(), item.SubStatus, item.ExecutionSequence,
        item.ScheduledAt, item.StartedAt, item.CompletedAt, item.ParentActivityExecutionId,
        item.SchedulingActivityExecutionId, item.RelativeDepth, item.BranchId, item.IterationId, item.OutcomeNames,
        item.BookmarkCount, item.IncidentCount, item.BlockingIncidentCount, ActivityExecutionAttemptView.From(item.Attempt),
        ActivityExecutionBoundaryView.From(item.Boundary), ActivityExecutionInspectionDisclosure.EmptyMetadata);
}

public sealed record ActivityExecutionLayoutView(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ArtifactId,
    string SourceReferenceId,
    string Selection,
    IReadOnlyList<ActivityInvocationOriginSegmentView> BoundaryOrigin,
    string TemplateHash,
    IReadOnlyList<ExecutableActivityLayoutRecord> Nodes,
    IReadOnlyList<ActivityExecutionLayoutConnection> Connections,
    IReadOnlyList<ActivityExecutionNestedBoundaryLayout> NestedBoundaries)
{
    public static ActivityExecutionLayoutView From(ActivityExecutionLayout layout) => new(
        layout.WorkflowExecutionId, layout.ActivityExecutionId, layout.ArtifactId, layout.SourceReferenceId,
        layout.Selection.ToString(), layout.BoundaryOrigin.Segments.Select(ActivityInvocationOriginSegmentView.From).ToArray(), layout.TemplateHash, layout.Nodes, layout.Connections,
        layout.NestedBoundaries);
}

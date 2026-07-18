using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

public static class ActivityExecutionHierarchyPager
{
    public const int DefaultLimit = 100;
    public const int MaximumLimit = 500;

    public static ActivityExecutionHierarchyPage? Read(
        ActivityExecutionHierarchyQuery query,
        IReadOnlyCollection<ActivityExecutionHierarchyRecord> allRecords,
        IActivityExecutionHierarchyCursorCodec cursorCodec)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.RootActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.AuthorizationProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantScope);
        if (query.Limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "The hierarchy page limit must be positive.");

        var workflowRecords = allRecords.Where(x => StringComparer.Ordinal.Equals(x.WorkflowExecutionId, query.WorkflowExecutionId)).ToArray();
        var root = ActivityExecutionHierarchyProjector.FindRoot(workflowRecords, query.WorkflowExecutionId, query.RootActivityExecutionId);
        if (root is null)
            return null;

        var include = query.Include.Order().ToArray();
        var cursor = query.Cursor is null ? null : cursorCodec.Decode(query.Cursor);
        var effectiveLimit = cursor?.EffectiveLimit ?? Math.Min(query.Limit ?? DefaultLimit, MaximumLimit);
        if (cursor is not null)
            ValidateBinding(cursor, query, include, effectiveLimit);
        var scoped = workflowRecords.Where(x => StringComparer.Ordinal.Equals(x.ExecutionScopeId, query.RootActivityExecutionId)).ToArray();
        var currentWatermark = workflowRecords.Select(x => x.ExecutionSequence).DefaultIfEmpty(0).Max();
        var watermark = cursor?.CommittedThroughSequence ?? currentWatermark;
        if (cursor is not null && currentWatermark < watermark)
            throw new ActivityExecutionHierarchyCursorException(
                ActivityExecutionHierarchyCursorFailure.Expired,
                "The committed hierarchy snapshot is no longer available.",
                metadata: Metadata(
                    ActivityExecutionCursorBindingState.Matched,
                    ActivityExecutionCursorBindingState.Matched,
                    ActivityExecutionCursorBindingState.Matched,
                    "restart-from-first-page"));

        var snapshotRecords = workflowRecords.Where(x => x.ExecutionSequence <= watermark).ToArray();
        var itemsById = ActivityExecutionHierarchyProjector.ProjectItems(snapshotRecords, query.RootActivityExecutionId, query.Include)
            .ToDictionary(x => x.ActivityExecutionId, StringComparer.Ordinal);
        var ordered = scoped
            .Where(x => !StringComparer.Ordinal.Equals(x.ActivityExecutionId, query.RootActivityExecutionId))
            .Where(x => x.ExecutionSequence <= watermark)
            .Where(x => cursor is null || IsAfter(x, cursor.LastExecutionSequence, cursor.LastActivityExecutionId))
            .OrderBy(x => x.ExecutionSequence)
            .ThenBy(x => x.ActivityExecutionId, StringComparer.Ordinal)
            .Take(effectiveLimit + 1)
            .ToArray();
        var selected = ordered.Take(effectiveLimit).ToArray();
        var last = selected.LastOrDefault();
        var next = ordered.Length > effectiveLimit && last is not null
            ? cursorCodec.Encode(new(
                query.TenantScope,
                query.AuthorizationProfile,
                query.WorkflowExecutionId,
                query.RootActivityExecutionId,
                include,
                effectiveLimit,
                watermark,
                last.ExecutionSequence,
                last.ActivityExecutionId))
            : null;
        return new(root, watermark, effectiveLimit, selected.Select(x => itemsById[x.ActivityExecutionId]).ToArray(), next);
    }

    private static void ValidateBinding(
        ActivityExecutionHierarchyCursorState cursor,
        ActivityExecutionHierarchyQuery query,
        ActivityExecutionHierarchyInclude[] include,
        int effectiveLimit)
    {
        var accessMatches =
            StringComparer.Ordinal.Equals(cursor.TenantScope, query.TenantScope) &&
            StringComparer.Ordinal.Equals(cursor.AuthorizationProfile, query.AuthorizationProfile);
        var boundaryMatches =
            StringComparer.Ordinal.Equals(cursor.WorkflowExecutionId, query.WorkflowExecutionId) &&
            StringComparer.Ordinal.Equals(cursor.RootActivityExecutionId, query.RootActivityExecutionId);
        var queryMatches =
            cursor.EffectiveLimit == effectiveLimit &&
            (query.Limit is null || Math.Min(query.Limit.Value, MaximumLimit) == effectiveLimit) &&
            cursor.Include.SequenceEqual(include);
        if (!accessMatches || !boundaryMatches || !queryMatches)
            throw new ActivityExecutionHierarchyCursorException(
                ActivityExecutionHierarchyCursorFailure.BindingMismatch,
                "The hierarchy cursor belongs to another query or authorization scope.",
                metadata: Metadata(
                    State(boundaryMatches),
                    State(queryMatches),
                    State(accessMatches),
                    "restart-from-first-page"));
    }

    private static bool IsAfter(ActivityExecutionHierarchyRecord record, long sequence, string activityExecutionId) =>
        record.ExecutionSequence > sequence || record.ExecutionSequence == sequence && StringComparer.Ordinal.Compare(record.ActivityExecutionId, activityExecutionId) > 0;

    private static ActivityExecutionCursorBindingState State(bool matches) =>
        matches ? ActivityExecutionCursorBindingState.Matched : ActivityExecutionCursorBindingState.Mismatched;

    private static ActivityExecutionCursorFailureMetadata Metadata(
        ActivityExecutionCursorBindingState boundary,
        ActivityExecutionCursorBindingState query,
        ActivityExecutionCursorBindingState access,
        string recoveryAction) =>
        new("activity-execution-hierarchy", boundary, query, access, true, recoveryAction);
}

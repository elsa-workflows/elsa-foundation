using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Builds safe hierarchy records and aggregates from committed Runtime inspection evidence.</summary>
public static class ActivityExecutionHierarchyProjector
{
    public static ActivityExecutionHierarchyRecord FromInspection(ActivityExecutionInspectionProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var scopeId = projection.ExecutionScopeId ?? projection.Provenance.ExecutionScopeId;
        if (string.IsNullOrWhiteSpace(scopeId))
            throw new ArgumentException("Committed hierarchy evidence requires an execution scope id.", nameof(projection));

        var item = new ActivityExecutionHierarchyItem(
            projection.ActivityExecutionId,
            projection.WorkflowExecutionId,
            projection.ExecutableNodeId,
            projection.AuthoredActivityId,
            projection.ActivityType,
            projection.ActivityTypeVersion,
            projection.Status,
            projection.SubStatus,
            projection.ExecutionSequence,
            projection.ScheduledAt,
            projection.StartedAt,
            projection.CompletedAt,
            projection.Provenance.ParentActivityExecutionId,
            projection.Provenance.SchedulingActivityExecutionId,
            0,
            projection.Provenance.BranchId,
            projection.Provenance.IterationId,
            projection.OutcomeNames,
            projection.Bookmarks.Count,
            projection.Incidents.Count,
            projection.Incidents.Count(x => x.IsBlocking),
            projection.Attempt ?? projection.Provenance.Attempt,
            null,
            projection.Metadata);
        return new(projection.WorkflowExecutionId, scopeId, projection.ActivityExecutionId, item.ParentActivityExecutionId, projection.ExecutionSequence, item);
    }

    public static IReadOnlyList<ActivityExecutionHierarchyItem> ProjectItems(
        IReadOnlyCollection<ActivityExecutionHierarchyRecord> workflowRecords,
        string executionScopeId,
        IReadOnlySet<ActivityExecutionHierarchyInclude> include)
    {
        var scoped = workflowRecords
            .Where(x => StringComparer.Ordinal.Equals(x.ExecutionScopeId, executionScopeId))
            .Where(x => !StringComparer.Ordinal.Equals(x.ActivityExecutionId, executionScopeId))
            .ToArray();
        var depths = ComputeRelativeDepths(scoped, executionScopeId);
        return scoped.Select(record =>
        {
            var item = record.Item with
            {
                RelativeDepth = depths[record.ActivityExecutionId],
                OutcomeNames = include.Contains(ActivityExecutionHierarchyInclude.Outcomes) ? record.Item.OutcomeNames : [],
                BookmarkCount = include.Contains(ActivityExecutionHierarchyInclude.Bookmarks) ? record.Item.BookmarkCount : 0,
                IncidentCount = include.Contains(ActivityExecutionHierarchyInclude.Incidents) ? record.Item.IncidentCount : 0,
                BlockingIncidentCount = include.Contains(ActivityExecutionHierarchyInclude.Incidents) ? record.Item.BlockingIncidentCount : 0
            };
            return item with { Boundary = TryBuildBoundary(record.Item, workflowRecords) };
        }).ToArray();
    }

    public static ActivityExecutionBoundary? FindBoundary(
        IReadOnlyCollection<ActivityExecutionHierarchyRecord> workflowRecords,
        string activityExecutionId)
    {
        var record = workflowRecords.FirstOrDefault(x => StringComparer.Ordinal.Equals(x.ActivityExecutionId, activityExecutionId));
        return record is null ? null : TryBuildBoundary(record.Item, workflowRecords);
    }

    public static ActivityExecutionAttemptNavigation? FindAttemptNavigation(
        IReadOnlyCollection<ActivityExecutionHierarchyRecord> workflowRecords,
        string activityExecutionId)
    {
        var current = workflowRecords
            .Select(x => x.Item)
            .FirstOrDefault(x => StringComparer.Ordinal.Equals(x.ActivityExecutionId, activityExecutionId));
        if (current?.Attempt is not { } lineage)
            return null;

        var attempts = workflowRecords
            .Select(x => x.Item)
            .Where(x => x.Attempt is not null &&
                        StringComparer.Ordinal.Equals(
                            x.Attempt.FirstAttemptActivityExecutionId,
                            lineage.FirstAttemptActivityExecutionId))
            .OrderBy(x => x.Attempt!.AttemptNumber)
            .ThenBy(x => x.ExecutionSequence)
            .ThenBy(x => x.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();
        var index = Array.FindIndex(attempts, x => StringComparer.Ordinal.Equals(x.ActivityExecutionId, activityExecutionId));
        if (index < 0)
            return null;
        return new(lineage, index + 1 < attempts.Length ? attempts[index + 1].ActivityExecutionId : null, attempts.Length);
    }

    public static ActivityExecutionHierarchyRoot? FindRoot(
        IReadOnlyCollection<ActivityExecutionHierarchyRecord> workflowRecords,
        string workflowExecutionId,
        string activityExecutionId)
    {
        var item = workflowRecords.FirstOrDefault(x =>
            StringComparer.Ordinal.Equals(x.WorkflowExecutionId, workflowExecutionId) &&
            StringComparer.Ordinal.Equals(x.ActivityExecutionId, activityExecutionId))?.Item;
        var boundary = item is null ? null : TryBuildBoundary(item, workflowRecords);
        return boundary is null
            ? null
            : new(workflowExecutionId, activityExecutionId, boundary.ExecutionScopeId, boundary.DefinitionVersionId, boundary.TemplateHash);
    }

    private static Dictionary<string, int> ComputeRelativeDepths(
        IReadOnlyCollection<ActivityExecutionHierarchyRecord> records,
        string rootActivityExecutionId)
    {
        var byId = records.ToDictionary(x => x.ActivityExecutionId, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (depths.ContainsKey(record.ActivityExecutionId))
                continue;

            var path = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var currentId = record.ActivityExecutionId;
            var baseDepth = 0;
            while (true)
            {
                if (depths.TryGetValue(currentId, out var known))
                {
                    baseDepth = known;
                    break;
                }
                if (!seen.Add(currentId))
                    throw new InvalidOperationException("Committed activity execution hierarchy contains a parent cycle.");
                if (!byId.TryGetValue(currentId, out var current))
                    break;
                path.Add(currentId);
                var parent = current.ParentActivityExecutionId;
                if (parent is null || StringComparer.Ordinal.Equals(parent, rootActivityExecutionId))
                    break;
                currentId = parent;
            }

            for (var index = path.Count - 1; index >= 0; index--)
                depths[path[index]] = ++baseDepth;
        }
        return depths;
    }

    private static ActivityExecutionBoundary? TryBuildBoundary(
        ActivityExecutionHierarchyItem item,
        IReadOnlyCollection<ActivityExecutionHierarchyRecord> workflowRecords)
    {
        if (!TryRead(item.Metadata, "activity.definitionId", out var definitionId) ||
            !TryRead(item.Metadata, "activity.definitionVersionId", out var definitionVersionId) ||
            !TryRead(item.Metadata, "activity.version", out var version) ||
            !TryRead(item.Metadata, "activity.templateHash", out var templateHash))
            return null;

        var descendants = workflowRecords
            .Where(x => StringComparer.Ordinal.Equals(x.ExecutionScopeId, item.ActivityExecutionId))
            .Where(x => !StringComparer.Ordinal.Equals(x.ActivityExecutionId, item.ActivityExecutionId))
            .Select(x => x.Item)
            .ToArray();
        var directCount = descendants.Count(x => StringComparer.Ordinal.Equals(x.ParentActivityExecutionId, item.ActivityExecutionId));
        return new(
            "ActivityGraph",
            definitionId,
            definitionVersionId,
            version,
            templateHash,
            ParseOrigin(item.Metadata),
            item.ActivityExecutionId,
            descendants.Length > 0,
            directCount,
            descendants.LongLength,
            Aggregate(descendants),
            item.Metadata.ContainsKey("activity.sourceReferenceId"),
            item.ExecutableNodeId);
    }

    private static ActivityExecutionHierarchyAggregate Aggregate(IReadOnlyCollection<ActivityExecutionHierarchyItem> items)
    {
        var scheduled = items.LongCount(x => x.Status == ActivityExecutionStatus.Scheduled);
        var running = items.LongCount(x => x.Status is ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting);
        var suspended = items.LongCount(x => x.Status == ActivityExecutionStatus.Suspended);
        var completed = items.LongCount(x => x.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Recovered or ActivityExecutionStatus.Superseded);
        var faulted = items.LongCount(x => x.Status == ActivityExecutionStatus.Faulted);
        var cancelled = items.LongCount(x => x.Status == ActivityExecutionStatus.Cancelled);
        var blocking = items.Sum(x => (long)x.BlockingIncidentCount);
        var status = blocking > 0 ? ActivityExecutionHierarchyAggregateStatus.Faulted
            : cancelled > 0 && scheduled + running == 0 ? ActivityExecutionHierarchyAggregateStatus.Cancelled
            : suspended > 0 && scheduled + running == 0 ? ActivityExecutionHierarchyAggregateStatus.Suspended
            : scheduled + running > 0 ? ActivityExecutionHierarchyAggregateStatus.Running
            : items.Count == 0 ? ActivityExecutionHierarchyAggregateStatus.Empty
            : ActivityExecutionHierarchyAggregateStatus.Completed;
        return new(
            status,
            items.Count,
            scheduled,
            running,
            suspended,
            completed,
            faulted,
            cancelled,
            blocking,
            items.LongCount(x => x.Attempt is { AttemptNumber: > 1 }),
            items.Select(x => x.ExecutionSequence).DefaultIfEmpty(0).Max());
    }

    private static ActivityInvocationOrigin ParseOrigin(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue("activity.invocationOrigin", out var value))
            return ActivityInvocationOrigin.Empty;
        try
        {
            return new(JsonSerializer.Deserialize<ActivityInvocationOriginSegment[]>(value) ?? []);
        }
        catch (JsonException)
        {
            return ActivityInvocationOrigin.Empty;
        }
    }

    private static bool TryRead(IReadOnlyDictionary<string, string> metadata, string key, out string value)
    {
        if (metadata.TryGetValue(key, out var candidate) && !string.IsNullOrWhiteSpace(candidate))
        {
            value = candidate;
            return true;
        }
        value = string.Empty;
        return false;
    }
}

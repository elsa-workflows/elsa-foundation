using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Compares the complete persisted payload of a workflow executable source reference.
/// </summary>
/// <remarks>
/// Source-reference compensation may change only retirement facts. Every other persisted value, including opaque
/// layout and authored-input sidecars, participates in the comparison so a superseding writer can never be replaced
/// by a compensation write based on a stale snapshot.
/// </remarks>
public static class WorkflowExecutableSourceReferenceComparer
{
    /// <summary>Returns whether two references have identical persisted identity and source payload.</summary>
    public static bool SameIdentity(
        WorkflowExecutableSourceReference left,
        WorkflowExecutableSourceReference right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return StringComparer.Ordinal.Equals(left.SourceReferenceId, right.SourceReferenceId) &&
               StringComparer.Ordinal.Equals(left.ArtifactId, right.ArtifactId) &&
               StringComparer.Ordinal.Equals(left.SourceKind, right.SourceKind) &&
               StringComparer.Ordinal.Equals(left.SourceId, right.SourceId) &&
               StringComparer.Ordinal.Equals(left.SourceVersion, right.SourceVersion) &&
               StringComparer.Ordinal.Equals(left.DefinitionId, right.DefinitionId) &&
               StringComparer.Ordinal.Equals(left.DefinitionVersionId, right.DefinitionVersionId) &&
               StringComparer.Ordinal.Equals(left.ArtifactVersion, right.ArtifactVersion) &&
               left.CreatedAt == right.CreatedAt &&
               left.PublishedAt == right.PublishedAt &&
               left.Scope == right.Scope &&
               left.ExpiresAt == right.ExpiresAt &&
               StringComparer.Ordinal.Equals(left.ActivationId, right.ActivationId) &&
               StringComparer.Ordinal.Equals(left.SlotId, right.SlotId) &&
               StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) &&
               SameLayout(left.Layout, right.Layout) &&
               SameSidecar(left.LayoutSidecar, right.LayoutSidecar) &&
               SameAuthoredInputs(left.AuthoredInputs, right.AuthoredInputs) &&
               SameActivityPresentation(left.ActivityPresentation, right.ActivityPresentation);
    }

    /// <summary>Returns whether two references are identical in every persisted field.</summary>
    public static bool SameSnapshot(
        WorkflowExecutableSourceReference left,
        WorkflowExecutableSourceReference right) =>
        SameIdentity(left, right) &&
        left.DeletedAt == right.DeletedAt &&
        StringComparer.Ordinal.Equals(left.DeletedReason, right.DeletedReason);

    private static bool SameLayout(
        IReadOnlyList<WorkflowExecutableLayoutRecord> left,
        IReadOnlyList<WorkflowExecutableLayoutRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var leftRecord = left[i];
            var rightRecord = right[i];
            if (!StringComparer.Ordinal.Equals(leftRecord.NodeId, rightRecord.NodeId) ||
                leftRecord.X != rightRecord.X ||
                leftRecord.Y != rightRecord.Y ||
                leftRecord.Width != rightRecord.Width ||
                leftRecord.Height != rightRecord.Height ||
                !SameJson(leftRecord.AdditionalProperties, rightRecord.AdditionalProperties))
                return false;
        }

        return true;
    }

    private static bool SameSidecar(ExecutableLayoutSidecar left, ExecutableLayoutSidecar right)
    {
        if (left.BoundarySegments.Count != right.BoundarySegments.Count)
            return false;

        for (var i = 0; i < left.BoundarySegments.Count; i++)
        {
            var leftSegment = left.BoundarySegments[i];
            var rightSegment = right.BoundarySegments[i];
            if (!StringComparer.Ordinal.Equals(leftSegment.BoundarySegmentId, rightSegment.BoundarySegmentId) ||
                !SameOrigin(leftSegment.BoundaryOrigin, rightSegment.BoundaryOrigin) ||
                !StringComparer.Ordinal.Equals(leftSegment.TemplateHash, rightSegment.TemplateHash) ||
                !SameActivityLayout(leftSegment.Records, rightSegment.Records) ||
                !SameOrigins(leftSegment.NestedBoundaryOrigins, rightSegment.NestedBoundaryOrigins))
                return false;
        }

        return true;
    }

    private static bool SameActivityLayout(
        IReadOnlyList<ExecutableActivityLayoutRecord> left,
        IReadOnlyList<ExecutableActivityLayoutRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var leftRecord = left[i];
            var rightRecord = right[i];
            if (!StringComparer.Ordinal.Equals(leftRecord.TemplateNodeId, rightRecord.TemplateNodeId) ||
                !StringComparer.Ordinal.Equals(leftRecord.AuthoredActivityId, rightRecord.AuthoredActivityId) ||
                !StringComparer.Ordinal.Equals(leftRecord.ExecutableNodeId, rightRecord.ExecutableNodeId) ||
                leftRecord.X != rightRecord.X ||
                leftRecord.Y != rightRecord.Y ||
                leftRecord.Width != rightRecord.Width ||
                leftRecord.Height != rightRecord.Height ||
                !SameJson(leftRecord.AdditionalProperties, rightRecord.AdditionalProperties) ||
                !StringComparer.Ordinal.Equals(leftRecord.ActivityType, rightRecord.ActivityType) ||
                !StringComparer.Ordinal.Equals(leftRecord.ActivityTypeVersion, rightRecord.ActivityTypeVersion) ||
                leftRecord.HasPinnedGeometry != rightRecord.HasPinnedGeometry)
                return false;
        }

        return true;
    }

    private static bool SameOrigins(
        IReadOnlyList<ActivityInvocationOrigin> left,
        IReadOnlyList<ActivityInvocationOrigin> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!SameOrigin(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static bool SameOrigin(ActivityInvocationOrigin left, ActivityInvocationOrigin right)
    {
        if (left.Segments.Count != right.Segments.Count)
            return false;

        for (var i = 0; i < left.Segments.Count; i++)
        {
            var leftSegment = left.Segments[i];
            var rightSegment = right.Segments[i];
            if (leftSegment.Kind != rightSegment.Kind ||
                !StringComparer.Ordinal.Equals(leftSegment.Id, rightSegment.Id))
                return false;
        }

        return true;
    }

    private static bool SameAuthoredInputs(
        IReadOnlyList<WorkflowExecutableAuthoredInputRecord> left,
        IReadOnlyList<WorkflowExecutableAuthoredInputRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var leftRecord = left[i];
            var rightRecord = right[i];
            if (!StringComparer.Ordinal.Equals(leftRecord.ExecutableNodeId, rightRecord.ExecutableNodeId) ||
                !StringComparer.Ordinal.Equals(leftRecord.InputKey, rightRecord.InputKey) ||
                !StringComparer.Ordinal.Equals(leftRecord.ExpressionType, rightRecord.ExpressionType) ||
                !JsonElement.DeepEquals(leftRecord.Value, rightRecord.Value) ||
                leftRecord.IsSensitive != rightRecord.IsSensitive)
                return false;
        }

        return true;
    }

    private static bool SameActivityPresentation(
        IReadOnlyList<WorkflowExecutableActivityPresentationRecord> left,
        IReadOnlyList<WorkflowExecutableActivityPresentationRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var leftRecord = left[i];
            var rightRecord = right[i];
            if (!StringComparer.Ordinal.Equals(leftRecord.ExecutableNodeId, rightRecord.ExecutableNodeId) ||
                !StringComparer.Ordinal.Equals(leftRecord.DisplayName, rightRecord.DisplayName) ||
                !StringComparer.Ordinal.Equals(leftRecord.Description, rightRecord.Description))
                return false;
        }

        return true;
    }

    private static bool SameJson(JsonElement? left, JsonElement? right) =>
        left.HasValue == right.HasValue &&
        (!left.HasValue || JsonElement.DeepEquals(left.Value, right!.Value));
}

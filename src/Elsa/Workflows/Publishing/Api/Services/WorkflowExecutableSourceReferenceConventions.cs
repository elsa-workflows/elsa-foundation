using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Canonical SourceKind names carried on <see cref="WorkflowExecutableSourceReference"/> records.</summary>
public static class WorkflowExecutableSourceKinds
{
    public const string WorkflowDefinitionVersion = "WorkflowDefinitionVersion";
    public const string WorkflowDraftSnapshot = "WorkflowDraftSnapshot";
}

/// <summary>
/// Verbatim copy of a definition version's layout into the reference sidecar (ADR 0039), shared by the publish and
/// test-run flows. The design records map 1:1 into the runtime-owned layout type; AdditionalProperties travels
/// opaquely (ADR 0035).
/// </summary>
public static class WorkflowExecutableLayoutSidecar
{
    public static IReadOnlyList<WorkflowExecutableLayoutRecord> CopyFrom(WorkflowDefinitionVersionLayout? layout) =>
        layout is null
            ? []
            : layout.Records
                .Select(record => new WorkflowExecutableLayoutRecord(
                    record.NodeId,
                    record.X,
                    record.Y,
                    record.Width,
                    record.Height,
                    record.AdditionalProperties))
                .ToArray();
}

/// <summary>
/// Projects authored activity presentation onto the flattened executable identities used by
/// historical inspection. Presentation never participates in executable hashing.
/// </summary>
public static class WorkflowExecutableActivityPresentationSidecar
{
    public static IReadOnlyList<WorkflowExecutableActivityPresentationRecord> CopyFrom(
        IEnumerable<ActivityPresentationRecord>? records,
        WorkflowExecutable executable)
    {
        var byAuthoredId = executable.Nodes
            .Append(executable.RootActivity)
            .DistinctBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
            .Where(node => !string.IsNullOrWhiteSpace(node.AuthoredActivityId))
            .GroupBy(node => node.AuthoredActivityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var result = new List<WorkflowExecutableActivityPresentationRecord>();
        foreach (var record in ActivityPresentationRecord.NormalizeCollection(records))
        {
            if (!byAuthoredId.TryGetValue(record.NodeId, out var nodes))
                continue;

            result.AddRange(nodes.Select(node => new WorkflowExecutableActivityPresentationRecord(
                node.ExecutableNodeId,
                record.DisplayName,
                record.Description)));
        }

        return result;
    }
}

/// <summary>
/// Projects authored input sources into the per-publish source-reference sidecar. The activity tree projection is
/// shared with executable compilation so inputs nested in activity-owned structures are included as well as the root.
/// </summary>
public sealed class WorkflowExecutableAuthoredInputsSidecar(ActivityTreeProjector activityTreeProjector)
{
    public IReadOnlyList<WorkflowExecutableAuthoredInputRecord> CopyFrom(WorkflowDefinitionState state)
    {
        if (state.RootActivity is null)
            return [];

        return activityTreeProjector.Project(state.RootActivity).Nodes
            .SelectMany(node => node.Inputs.Select(input => new WorkflowExecutableAuthoredInputRecord(
                node.NodeId,
                input.ReferenceKey,
                input.Value.ExpressionType,
                CopyValue(input.Value.Value),
                input.IsSensitive is true)))
            .ToArray();
    }

    private static JsonElement CopyValue(object? value) =>
        value is JsonElement json ? json.Clone() : JsonSerializer.SerializeToElement(value);
}

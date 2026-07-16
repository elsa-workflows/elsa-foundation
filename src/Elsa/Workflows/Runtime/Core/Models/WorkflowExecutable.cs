using System.Collections.ObjectModel;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A content-addressed, fully immutable workflow executable artifact (ADR 0038): pure behavior, one row per
/// distinct behavior. Per-publish facts — scope, expiry, published/deleted timestamps, source provenance and the
/// layout sidecar — do NOT live here; they belong to the <see cref="WorkflowExecutableSourceReference"/> records
/// that point at this artifact.
/// </summary>
public sealed class WorkflowExecutable
{
    public WorkflowExecutable(
        WorkflowExecutableIdentity identity,
        ExecutableNode rootActivity,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string> compatibilityMetadata,
        IReadOnlyCollection<RuntimeRequirement>? runtimeRequirements = null,
        IReadOnlyCollection<RuntimeStorageDriverRequirement>? storageDriverRequirements = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(rootActivity);
        ArgumentNullException.ThrowIfNull(resumeTargets);
        ArgumentNullException.ThrowIfNull(compatibilityMetadata);

        var nodeSnapshot = Flatten(rootActivity).ToArray();

        Identity = identity;
        RootActivity = rootActivity;
        Nodes = Array.AsReadOnly(nodeSnapshot);
        NodesById = new ReadOnlyDictionary<string, ExecutableNode>(nodeSnapshot.ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal));
        ResumeTargets = new ReadOnlyDictionary<string, WorkflowExecutableResumeTarget>(resumeTargets.ToDictionary(target => target.Key, target => target.Value, StringComparer.Ordinal));
        CreatedAt = createdAt;
        CompatibilityMetadata = new ReadOnlyDictionary<string, string>(compatibilityMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        RuntimeRequirements = Array.AsReadOnly((runtimeRequirements ?? [])
            .Concat(nodeSnapshot.Select(node => new RuntimeRequirement(
                node.Descriptor.ConsumerKey,
                node.Descriptor.SchemaVersion)))
            .Distinct()
            .OrderBy(item => item.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(item => item.SchemaVersion, StringComparer.Ordinal)
            .ToArray());
        StorageDriverRequirements = Array.AsReadOnly((storageDriverRequirements ?? [])
            .Concat(nodeSnapshot.SelectMany(node => node.OutputCaptures.Values)
                .Select(capture => new RuntimeStorageDriverRequirement(capture.StorageDriverKey)))
            .Distinct()
            .OrderBy(item => item.DriverKey, StringComparer.Ordinal)
            .ToArray());
    }

    public WorkflowExecutableIdentity Identity { get; }
    public ExecutableNode RootActivity { get; }
    public IReadOnlyCollection<ExecutableNode> Nodes { get; }
    public IReadOnlyDictionary<string, ExecutableNode> NodesById { get; }
    public IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyDictionary<string, string> CompatibilityMetadata { get; }
    public IReadOnlyCollection<RuntimeRequirement> RuntimeRequirements { get; }
    public IReadOnlyCollection<RuntimeStorageDriverRequirement> StorageDriverRequirements { get; }

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode rootActivity)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}

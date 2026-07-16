using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

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
        IReadOnlyDictionary<string, string> compatibilityMetadata)
        : this(
            identity,
            rootActivity,
            resumeTargets,
            createdAt,
            compatibilityMetadata,
            inputContract: null,
            dependencies: null)
    {
    }

    [JsonConstructor]
    public WorkflowExecutable(
        WorkflowExecutableIdentity identity,
        ExecutableNode rootActivity,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string> compatibilityMetadata,
        WorkflowExecutableInputContract? inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency>? dependencies)
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
        InputContract = inputContract;
        Dependencies = SnapshotDependencies(dependencies, NodesById);
        CreatedAt = createdAt;
        CompatibilityMetadata = new ReadOnlyDictionary<string, string>(compatibilityMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    public WorkflowExecutableIdentity Identity { get; }
    public ExecutableNode RootActivity { get; }
    public IReadOnlyCollection<ExecutableNode> Nodes { get; }
    public IReadOnlyDictionary<string, ExecutableNode> NodesById { get; }
    public IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets { get; }
    public WorkflowExecutableInputContract? InputContract { get; }
    public IReadOnlyCollection<WorkflowExecutableDependency> Dependencies { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyDictionary<string, string> CompatibilityMetadata { get; }

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

    private static IReadOnlyCollection<WorkflowExecutableDependency> SnapshotDependencies(
        IReadOnlyCollection<WorkflowExecutableDependency>? dependencies,
        IReadOnlyDictionary<string, ExecutableNode> nodesById)
    {
        if (dependencies is null || dependencies.Count == 0)
            return Array.AsReadOnly(Array.Empty<WorkflowExecutableDependency>());

        var snapshot = dependencies
            .Select(dependency => dependency ?? throw new ArgumentException("Executable dependencies cannot contain null entries.", nameof(dependencies)))
            .OrderBy(dependency => dependency.ArtifactId, StringComparer.Ordinal)
            .ToArray();
        var duplicateArtifactId = snapshot
            .GroupBy(dependency => dependency.ArtifactId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateArtifactId is not null)
            throw new ArgumentException($"The direct executable dependency '{duplicateArtifactId}' is duplicated.", nameof(dependencies));

        foreach (var dependency in snapshot)
        {
            foreach (var nodeId in dependency.DispatchNodeIds)
            {
                if (!nodesById.ContainsKey(nodeId))
                    throw new ArgumentException($"Executable dependency '{dependency.ArtifactId}' binds unknown dispatch node '{nodeId}'.", nameof(dependencies));
            }
        }

        return Array.AsReadOnly(snapshot);
    }
}

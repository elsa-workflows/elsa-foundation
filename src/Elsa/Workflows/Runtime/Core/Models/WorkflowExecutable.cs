using System.Collections.ObjectModel;
using Elsa.Activities.Runtime.Core.Models;
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
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null)
    {
    }

    public WorkflowExecutable(
        WorkflowExecutableIdentity identity,
        ExecutableNode rootActivity,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string> compatibilityMetadata,
        IReadOnlyCollection<RuntimeRequirement>? runtimeRequirements = null,
        IReadOnlyCollection<RuntimeStorageDriverRequirement>? storageDriverRequirements = null)
        : this(
            identity,
            rootActivity,
            resumeTargets,
            createdAt,
            compatibilityMetadata,
            inputContract: null,
            dependencies: null,
            runtimeRequirements,
            storageDriverRequirements)
    {
    }

    public WorkflowExecutable(
        WorkflowExecutableIdentity identity,
        ExecutableNode rootActivity,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string> compatibilityMetadata,
        WorkflowExecutableInputContract? inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency>? dependencies)
        : this(
            identity,
            rootActivity,
            resumeTargets,
            createdAt,
            compatibilityMetadata,
            inputContract,
            dependencies,
            runtimeRequirements: null,
            storageDriverRequirements: null)
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
        IReadOnlyCollection<WorkflowExecutableDependency>? dependencies,
        IReadOnlyCollection<RuntimeRequirement>? runtimeRequirements,
        IReadOnlyCollection<RuntimeStorageDriverRequirement>? storageDriverRequirements,
        WorkflowExecutableCheckpointCadence? checkpointCadence = null,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables = null)
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
        CheckpointCadence = checkpointCadence;
        WorkflowVariables = SnapshotWorkflowVariables(workflowVariables);
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
    public WorkflowExecutableInputContract? InputContract { get; }
    public IReadOnlyCollection<WorkflowExecutableDependency> Dependencies { get; }

    /// <summary>
    /// The authored per-workflow checkpoint cadence compiled into this artifact (ADR 0032 R5), or <see langword="null"/>
    /// when the workflow authored none and the host default applies. Part of the behavioral content hash: a cadence
    /// change is a distinct executable identity, so it can never be silently aliased onto a same-graph artifact.
    /// </summary>
    public WorkflowExecutableCheckpointCadence? CheckpointCadence { get; }

    /// <summary>
    /// The workflow-scope variable declarations compiled from the authored definition's
    /// <c>state.Variables</c>. These seed the canonical root variable frame (scope id
    /// <c>"workflow"</c>) at execution start; container-structure variables are NOT included here —
    /// each container node (including the root node) owns those under its own node-id scope.
    /// Part of the behavioral content hash: they define the workflow frame's key set and defaults.
    /// </summary>
    public IReadOnlyCollection<RuntimeVariableDeclaration> WorkflowVariables { get; }

    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyDictionary<string, string> CompatibilityMetadata { get; }
    public IReadOnlyCollection<RuntimeRequirement> RuntimeRequirements { get; }
    public IReadOnlyCollection<RuntimeStorageDriverRequirement> StorageDriverRequirements { get; }

    private static IReadOnlyCollection<RuntimeVariableDeclaration> SnapshotWorkflowVariables(
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables)
    {
        if (workflowVariables is null || workflowVariables.Count == 0)
            return Array.AsReadOnly(Array.Empty<RuntimeVariableDeclaration>());

        var snapshot = workflowVariables
            .Select(variable => variable ?? throw new ArgumentException("Workflow variable declarations cannot contain null entries.", nameof(workflowVariables)))
            .OrderBy(variable => variable.VariableKey, StringComparer.Ordinal)
            .ToArray();
        var duplicateKey = snapshot
            .GroupBy(variable => variable.VariableKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateKey is not null)
            throw new ArgumentException($"The workflow variable declaration '{duplicateKey}' is duplicated.", nameof(workflowVariables));

        return Array.AsReadOnly(snapshot);
    }

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

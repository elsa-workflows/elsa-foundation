using System.Collections.ObjectModel;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned executable artifact produced by compile/publish and consumed by workflow execution.
/// </summary>
public sealed class WorkflowExecutable
{
    public WorkflowExecutable(
        WorkflowExecutableIdentity identity,
        ExecutableNode rootActivity,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        DateTimeOffset createdAt,
        DateTimeOffset? publishedAt,
        IReadOnlyDictionary<string, string> compatibilityMetadata)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(rootActivity);
        ArgumentNullException.ThrowIfNull(resumeTargets);
        ArgumentNullException.ThrowIfNull(compatibilityMetadata);

        ValidateComposition(rootActivity);
        var nodeSnapshot = Flatten(rootActivity).ToArray();

        Identity = identity;
        RootActivity = rootActivity;
        Nodes = Array.AsReadOnly(nodeSnapshot);
        NodesById = new ReadOnlyDictionary<string, ExecutableNode>(nodeSnapshot.ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal));
        ResumeTargets = new ReadOnlyDictionary<string, WorkflowExecutableResumeTarget>(resumeTargets.ToDictionary(target => target.Key, target => target.Value, StringComparer.Ordinal));
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
        CompatibilityMetadata = new ReadOnlyDictionary<string, string>(compatibilityMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    public WorkflowExecutableIdentity Identity { get; }
    public ExecutableNode RootActivity { get; }
    public IReadOnlyCollection<ExecutableNode> Nodes { get; }
    public IReadOnlyDictionary<string, ExecutableNode> NodesById { get; }
    public IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? PublishedAt { get; }
    public IReadOnlyDictionary<string, string> CompatibilityMetadata { get; }

    private static void ValidateComposition(ExecutableNode rootActivity)
    {
        foreach (var node in Flatten(rootActivity))
        {
            if (node.Composition is not { } composition)
                continue;

            var childIds = composition.Activities.Select(activity => activity.ExecutableNodeId).ToHashSet(StringComparer.Ordinal);
            foreach (var edge in composition.Connections)
            {
                if (!childIds.Contains(edge.SourceNodeId))
                    throw new ArgumentException($"Executable composition edge source node '{edge.SourceNodeId}' does not exist.");

                if (!childIds.Contains(edge.TargetNodeId))
                    throw new ArgumentException($"Executable composition edge target node '{edge.TargetNodeId}' does not exist.");
            }

            if (composition.StartActivityId is not null && !childIds.Contains(composition.StartActivityId))
                throw new ArgumentException($"Executable composition start activity '{composition.StartActivityId}' does not exist.");
        }
    }

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode rootActivity)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.Composition?.Activities ?? [])
                stack.Push(child);
        }
    }
}

/// <summary>
/// Exact executable artifact snapshot identity pinned by workflow executions.
/// </summary>
public sealed record WorkflowExecutableIdentity(
    string ArtifactId,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string ArtifactHash,
    WorkflowExecutableSourceReference? Source = null);

public static class WorkflowExecutableIdentityComparer
{
    public static bool MatchesPinnedSnapshot(WorkflowExecutableIdentity executable, WorkflowExecutableIdentity pinned)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(pinned);

        return executable.ArtifactId == pinned.ArtifactId &&
               executable.DefinitionId == pinned.DefinitionId &&
               executable.DefinitionVersionId == pinned.DefinitionVersionId &&
               executable.ArtifactVersion == pinned.ArtifactVersion &&
               executable.ArtifactHash == pinned.ArtifactHash;
    }

    public static string Format(WorkflowExecutableIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return $"{identity.ArtifactId}@{identity.ArtifactVersion}/{identity.ArtifactHash} ({identity.DefinitionId}/{identity.DefinitionVersionId})";
    }
}

/// <summary>
/// Foreign-key style source reference for diagnostics and migration tooling. Runtime execution does not load it.
/// </summary>
public sealed record WorkflowExecutableSourceReference(
    string SourceKind,
    string SourceId,
    string? SourceVersion = null);

/// <summary>
/// Runtime-owned control-flow edge inside a compiled composite activity.
/// </summary>
public sealed class ExecutableEdge
{
    public ExecutableEdge(
        string sourceNodeId,
        string sourcePort,
        string targetNodeId,
        string targetPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePort);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPort);

        SourceNodeId = sourceNodeId;
        SourcePort = sourcePort;
        TargetNodeId = targetNodeId;
        TargetPort = targetPort;
    }

    public string SourceNodeId { get; }
    public string SourcePort { get; }
    public string TargetNodeId { get; }
    public string TargetPort { get; }
}

/// <summary>
/// Compiled composition state owned by an executable activity.
/// </summary>
public sealed class ExecutableActivityComposition
{
    public ExecutableActivityComposition(
        IReadOnlyCollection<ExecutableNode> activities,
        IReadOnlyCollection<ExecutableEdge> connections,
        string? startActivityId = null)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(connections);

        Activities = Array.AsReadOnly(activities.ToArray());
        Connections = Array.AsReadOnly(connections.ToArray());
        StartActivityId = startActivityId;
    }

    public IReadOnlyCollection<ExecutableNode> Activities { get; }
    public IReadOnlyCollection<ExecutableEdge> Connections { get; }
    public string? StartActivityId { get; }
}

/// <summary>
/// Runtime-owned node inside a workflow executable.
/// </summary>
public sealed class ExecutableNode
{
    public ExecutableNode(
        string executableNodeId,
        string authoredActivityId,
        string activityType,
        string activityTypeVersion,
        string descriptorType,
        JsonElement descriptorPayload,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyDictionary<string, RuntimeOutputCapture> outputCaptures,
        IReadOnlyDictionary<string, string> metadata,
        ExecutableActivityComposition? composition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityTypeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorType);
        ArgumentNullException.ThrowIfNull(inputBindings);
        ArgumentNullException.ThrowIfNull(outputCaptures);
        ArgumentNullException.ThrowIfNull(metadata);

        var inputBindingSnapshot = inputBindings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var outputCaptureSnapshot = outputCaptures.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        foreach (var (inputName, binding) in inputBindingSnapshot)
        {
            if (!StringComparer.Ordinal.Equals(inputName, binding.InputName))
                throw new ArgumentException($"Input binding dictionary key '{inputName}' must match binding input name '{binding.InputName}'.", nameof(inputBindings));
        }

        foreach (var (outputName, capture) in outputCaptureSnapshot)
        {
            if (!StringComparer.Ordinal.Equals(outputName, capture.OutputName))
                throw new ArgumentException($"Output capture dictionary key '{outputName}' must match capture output name '{capture.OutputName}'.", nameof(outputCaptures));
        }

        ExecutableNodeId = executableNodeId;
        AuthoredActivityId = authoredActivityId;
        ActivityType = activityType;
        ActivityTypeVersion = activityTypeVersion;
        DescriptorType = descriptorType;
        DescriptorPayload = descriptorPayload.Clone();
        InputBindings = new ReadOnlyDictionary<string, RuntimeInputBinding>(inputBindingSnapshot);
        OutputCaptures = new ReadOnlyDictionary<string, RuntimeOutputCapture>(outputCaptureSnapshot);
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        Composition = composition;
    }

    public string ExecutableNodeId { get; }
    public string AuthoredActivityId { get; }
    public string ActivityType { get; }
    public string ActivityTypeVersion { get; }
    public string DescriptorType { get; }
    public JsonElement DescriptorPayload { get; }
    public IReadOnlyDictionary<string, RuntimeInputBinding> InputBindings { get; }
    public IReadOnlyDictionary<string, RuntimeOutputCapture> OutputCaptures { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public ExecutableActivityComposition? Composition { get; }
}

/// <summary>
/// Stable target inside a pinned executable artifact used to resolve durable resume handles.
/// </summary>
public sealed record WorkflowExecutableResumeTarget(
    string ResumeTargetId,
    string ExecutableNodeId,
    string HandlerKey,
    IReadOnlyDictionary<string, string> Metadata);

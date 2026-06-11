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
        IReadOnlyCollection<ExecutableNode> nodes,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        DateTimeOffset createdAt,
        DateTimeOffset? publishedAt,
        IReadOnlyDictionary<string, string> compatibilityMetadata)
        : this(identity, nodes, edges: [], startNodeIds: [], resumeTargets, createdAt, publishedAt, compatibilityMetadata)
    {
    }

    public WorkflowExecutable(
        WorkflowExecutableIdentity identity,
        IReadOnlyCollection<ExecutableNode> nodes,
        IReadOnlyCollection<ExecutableEdge>? edges,
        IReadOnlyCollection<string>? startNodeIds,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        DateTimeOffset createdAt,
        DateTimeOffset? publishedAt,
        IReadOnlyDictionary<string, string> compatibilityMetadata)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(resumeTargets);
        ArgumentNullException.ThrowIfNull(compatibilityMetadata);

        var nodeSnapshot = nodes.ToArray();
        var edgeSnapshot = (edges ?? []).ToArray();
        var nodeIds = nodeSnapshot.Select(node => node.ExecutableNodeId).ToHashSet(StringComparer.Ordinal);

        foreach (var edge in edgeSnapshot)
        {
            if (!nodeIds.Contains(edge.SourceNodeId))
                throw new ArgumentException($"Executable edge source node '{edge.SourceNodeId}' does not exist.", nameof(edges));

            if (!nodeIds.Contains(edge.TargetNodeId))
                throw new ArgumentException($"Executable edge target node '{edge.TargetNodeId}' does not exist.", nameof(edges));
        }

        var startSnapshot = (startNodeIds ?? []).ToArray();
        foreach (var startNodeId in startSnapshot)
        {
            if (!nodeIds.Contains(startNodeId))
                throw new ArgumentException($"Start node '{startNodeId}' does not exist.", nameof(startNodeIds));
        }

        Nodes = Array.AsReadOnly(nodeSnapshot);
        Edges = Array.AsReadOnly(edgeSnapshot);
        StartNodeIds = Array.AsReadOnly(startSnapshot);
        Identity = identity;
        NodesById = new ReadOnlyDictionary<string, ExecutableNode>(nodeSnapshot.ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal));
        ResumeTargets = new ReadOnlyDictionary<string, WorkflowExecutableResumeTarget>(resumeTargets.ToDictionary(target => target.Key, target => target.Value, StringComparer.Ordinal));
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
        CompatibilityMetadata = new ReadOnlyDictionary<string, string>(compatibilityMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    public WorkflowExecutableIdentity Identity { get; }
    public IReadOnlyCollection<ExecutableNode> Nodes { get; }
    public IReadOnlyCollection<ExecutableEdge> Edges { get; }
    public IReadOnlyCollection<string> StartNodeIds { get; }
    public IReadOnlyDictionary<string, ExecutableNode> NodesById { get; }
    public IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? PublishedAt { get; }
    public IReadOnlyDictionary<string, string> CompatibilityMetadata { get; }
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

internal static class WorkflowExecutableIdentityComparer
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
/// Runtime-owned control-flow edge between executable nodes.
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
        IReadOnlyDictionary<string, string> metadata)
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
}

/// <summary>
/// Stable target inside a pinned executable artifact used to resolve durable resume handles.
/// </summary>
public sealed record WorkflowExecutableResumeTarget(
    string ResumeTargetId,
    string ExecutableNodeId,
    string HandlerKey,
    IReadOnlyDictionary<string, string> Metadata);

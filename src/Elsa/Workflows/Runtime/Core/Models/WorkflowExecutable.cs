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
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(resumeTargets);
        ArgumentNullException.ThrowIfNull(compatibilityMetadata);

        var nodeSnapshot = nodes.ToArray();

        Nodes = Array.AsReadOnly(nodeSnapshot);
        Identity = identity;
        NodesById = new ReadOnlyDictionary<string, ExecutableNode>(nodeSnapshot.ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal));
        ResumeTargets = new ReadOnlyDictionary<string, WorkflowExecutableResumeTarget>(resumeTargets.ToDictionary(target => target.Key, target => target.Value, StringComparer.Ordinal));
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
        CompatibilityMetadata = new ReadOnlyDictionary<string, string>(compatibilityMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    public WorkflowExecutableIdentity Identity { get; }
    public IReadOnlyCollection<ExecutableNode> Nodes { get; }
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

/// <summary>
/// Foreign-key style source reference for diagnostics and migration tooling. Runtime execution does not load it.
/// </summary>
public sealed record WorkflowExecutableSourceReference(
    string SourceKind,
    string SourceId,
    string? SourceVersion = null);

/// <summary>
/// Runtime-owned node inside a workflow executable.
/// </summary>
public sealed class ExecutableNode
{
    public ExecutableNode(
        string ExecutableNodeId,
        string AuthoredActivityId,
        string ActivityType,
        string ActivityTypeVersion,
        string DescriptorType,
        JsonElement DescriptorPayload,
        IReadOnlyDictionary<string, RuntimeInputBinding> InputBindings,
        IReadOnlyDictionary<string, RuntimeOutputCapture> OutputCaptures,
        IReadOnlyDictionary<string, string> Metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuthoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActivityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActivityTypeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(DescriptorType);
        ArgumentNullException.ThrowIfNull(InputBindings);
        ArgumentNullException.ThrowIfNull(OutputCaptures);
        ArgumentNullException.ThrowIfNull(Metadata);

        this.ExecutableNodeId = ExecutableNodeId;
        this.AuthoredActivityId = AuthoredActivityId;
        this.ActivityType = ActivityType;
        this.ActivityTypeVersion = ActivityTypeVersion;
        this.DescriptorType = DescriptorType;
        this.DescriptorPayload = DescriptorPayload.Clone();
        this.InputBindings = new ReadOnlyDictionary<string, RuntimeInputBinding>(InputBindings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        this.OutputCaptures = new ReadOnlyDictionary<string, RuntimeOutputCapture>(OutputCaptures.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        this.Metadata = RuntimeModelMetadata.Snapshot(Metadata);
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

using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned executable artifact produced by compile/publish and consumed by workflow execution.
/// </summary>
public sealed record WorkflowExecutable
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

        Nodes = nodes.ToArray();
        Identity = identity;
        NodesById = Nodes.ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal);
        ResumeTargets = resumeTargets;
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
        CompatibilityMetadata = compatibilityMetadata;
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
public sealed record ExecutableNode(
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string DescriptorType,
    JsonElement DescriptorPayload,
    IReadOnlyDictionary<string, JsonElement> InputBindings,
    IReadOnlyDictionary<string, JsonElement> OutputCaptures,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Stable target inside a pinned executable artifact used to resolve durable resume handles.
/// </summary>
public sealed record WorkflowExecutableResumeTarget(
    string ResumeTargetId,
    string ExecutableNodeId,
    string HandlerKey,
    IReadOnlyDictionary<string, string> Metadata);

using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned executable artifact produced by compile/publish and consumed by workflow execution.
/// </summary>
public sealed record WorkflowExecutable(
    WorkflowExecutableIdentity Identity,
    IReadOnlyCollection<ExecutableNode> Nodes,
    IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyDictionary<string, string> CompatibilityMetadata);

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

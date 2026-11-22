namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One exact Workflow Executable as the Runtime inspection reads it (<see cref="Contracts.IWorkflowExecutableInspection"/>).</summary>
public sealed record WorkflowExecutableInspectionDetails(
    string ArtifactId,
    string ArtifactHash,
    DateTimeOffset CreatedAt,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int LiveSourceReferenceCount,
    WorkflowExecutableInspectionNode RootActivity,
    IReadOnlyList<WorkflowExecutableInspectionSourceReference> SourceReferences,
    IReadOnlyList<WorkflowExecutableInspectionDependency> Dependencies);

/// <summary>One compiled node: its identities, activity type, input bindings without values or source details, child slots and structure-owned connections.</summary>
public sealed record WorkflowExecutableInspectionNode(
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string? StructureKind,
    IReadOnlyList<WorkflowExecutableInspectionInput> Inputs,
    IReadOnlyList<WorkflowExecutableInspectionSlot> Slots,
    IReadOnlyList<WorkflowExecutableInspectionConnection> Connections);

/// <summary>
/// One compiled input binding: its name, its source kind and whether its policy hides the value. The binding's value and
/// every source detail (a secret read's reference included) are never part of it, as the Executable detail read keeps them out.
/// </summary>
public sealed record WorkflowExecutableInspectionInput(string Name, string Source, bool IsSensitive);

/// <summary>One child slot of a compiled node, with its nodes in order.</summary>
public sealed record WorkflowExecutableInspectionSlot(string Name, IReadOnlyList<WorkflowExecutableInspectionNode> Nodes);

/// <summary>One structure-owned connection between two child nodes, by executable node id and port.</summary>
public sealed record WorkflowExecutableInspectionConnection(string Source, string? SourcePort, string Target, string? TargetPort);

/// <summary>One Source Reference that keeps the Executable reachable: a publication or a test run.</summary>
public sealed record WorkflowExecutableInspectionSourceReference(
    string SourceReferenceId,
    string Scope,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DeletedAt,
    bool Live);

/// <summary>One direct executable dependency, by its own identity and Artifact Hash.</summary>
public sealed record WorkflowExecutableInspectionDependency(string ArtifactId, string ArtifactHash);
